using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mark.Core.Library;
using Mark.Core.Models;
using Microsoft.Data.Sqlite;

namespace Mark.Data;

/// <summary>
/// <see cref="ILibraryRepository"/> on <see cref="SqliteDatabase"/>. Every read orders explicitly (sort order, then id;
/// child rows by position) so the library, and every calculation made from it, never depends on storage order.
/// Decimals are stored as invariant text so prices round-trip exactly.
/// </summary>
public sealed class SqliteLibraryRepository : ILibraryRepository
{
    private readonly SqliteDatabase _database;

    /// <summary>JSON of systems, bundles, "used with" and reinforcement: camelCase, enums as names (the library file format).</summary>
    private static readonly JsonSerializerOptions DocumentOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public SqliteLibraryRepository(SqliteDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    // ── Read ────────────────────────────────────────────────────────

    public ProductLibrary Load() => _database.Guard("read the library", LoadCore);

    public bool IsEmpty() => _database.Guard("read the library", IsEmptyCore);

    private ProductLibrary LoadCore()
    {
        using var connection = _database.Connect();
        var roles = ReadChildren(connection, "SELECT profile_id, role FROM profile_roles ORDER BY profile_id, position",
            r => Enum.Parse<ProfileType>(r.GetString(1)));
        var stock = ReadChildren(connection, "SELECT profile_id, length_mm FROM profile_stock_lengths ORDER BY profile_id, position",
            r => r.GetDouble(1));
        var profileUsages = ReadChildren(connection,
            "SELECT profile_id, material_id, basis, quantity FROM profile_material_usages ORDER BY profile_id, position", ReadUsage);
        var glassUsages = ReadChildren(connection,
            "SELECT glass_id, material_id, basis, quantity FROM glass_material_usages ORDER BY glass_id, position", ReadUsage);

        var materials = ReadRows(connection,
            "SELECT id, name, code, manufacturer, category, unit, cost_per_unit, properties_json, is_active, used_with_json " +
            "FROM materials ORDER BY sort_order, id",
            r => new MaterialDefinition
            {
                Id = r.GetString(0),
                Name = r.GetString(1),
                Code = NullableString(r, 2),
                Manufacturer = NullableString(r, 3),
                Category = Enum.Parse<MaterialCategory>(r.GetString(4)),
                Unit = Enum.Parse<MaterialUnit>(r.GetString(5)),
                CostPerUnit = ParseMoney(r.GetString(6)),
                Properties = ReadProperties(r.GetString(7)),
                IsActive = r.GetInt64(8) != 0,
                UsedWith = Document<UsedWith>(r, 9)
            });

        var profiles = ReadRows(connection,
            "SELECT id, name, code, manufacturer, series, face_width_mm, depth_mm, weight_kg_per_m, cost_per_m, " +
            "stock_length_mm, cut_allowance_per_end_mm, glazing_bite_mm, properties_json, is_active, used_with_json, reinforcement_json " +
            "FROM profiles ORDER BY sort_order, id",
            r =>
            {
                string id = r.GetString(0);
                return new ProfileDefinition
                {
                    Id = id,
                    Name = r.GetString(1),
                    Code = NullableString(r, 2),
                    Manufacturer = NullableString(r, 3),
                    Series = NullableString(r, 4),
                    FaceWidthMm = r.GetDouble(5),
                    DepthMm = r.GetDouble(6),
                    WeightKgPerMetre = r.GetDouble(7),
                    CostPerMetre = ParseMoney(r.GetString(8)),
                    StockLengthMm = r.GetDouble(9),
                    CutAllowancePerEndMm = r.GetDouble(10),
                    GlazingBiteMm = r.GetDouble(11),
                    Properties = ReadProperties(r.GetString(12)),
                    IsActive = r.GetInt64(13) != 0,
                    UsedWith = Document<UsedWith>(r, 14),
                    Reinforcement = Document<ReinforcementRule>(r, 15),
                    Roles = ChildrenOf(roles, id),
                    StockLengthsMm = ChildrenOf(stock, id),
                    Materials = ChildrenOf(profileUsages, id)
                };
            });

        var glass = ReadRows(connection,
            "SELECT id, name, code, manufacturer, category, thickness_mm, cost_per_m2, weight_kg_per_m2, " +
            "min_chargeable_area_m2, properties_json, is_active, used_with_json FROM glass ORDER BY sort_order, id",
            r =>
            {
                string id = r.GetString(0);
                return new GlassDefinition
                {
                    Id = id,
                    Name = r.GetString(1),
                    Code = NullableString(r, 2),
                    Manufacturer = NullableString(r, 3),
                    Category = NullableString(r, 4),
                    ThicknessMm = r.GetDouble(5),
                    CostPerSquareMetre = ParseMoney(r.GetString(6)),
                    WeightKgPerSquareMetre = r.IsDBNull(7) ? null : r.GetDouble(7),
                    MinChargeableAreaM2 = r.GetDouble(8),
                    Properties = ReadProperties(r.GetString(9)),
                    IsActive = r.GetInt64(10) != 0,
                    UsedWith = Document<UsedWith>(r, 11),
                    Materials = ChildrenOf(glassUsages, id)
                };
            });

        var systems = ReadRows(connection, "SELECT definition_json FROM systems ORDER BY sort_order, id",
            r => JsonSerializer.Deserialize<ProductSystem>(r.GetString(0), DocumentOptions)!);
        var bundles = ReadRows(connection, "SELECT definition_json FROM bundles ORDER BY sort_order, id",
            r => JsonSerializer.Deserialize<Bundle>(r.GetString(0), DocumentOptions)!);

        var (currency, defaults) = ReadSettings(connection);
        try
        {
            return new ProductLibrary(profiles, glass, materials, defaults, currency, systems, bundles);
        }
        catch (LibraryValidationException ex)
        {
            throw new DataStoreException($"The library stored in the database is invalid: {string.Join(" ", ex.Errors)}", ex);
        }
    }

    private bool IsEmptyCore()
    {
        using var connection = _database.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM profiles) + (SELECT COUNT(*) FROM glass) + (SELECT COUNT(*) FROM materials) " +
                              "+ (SELECT COUNT(*) FROM systems) + (SELECT COUNT(*) FROM bundles)";
        return Convert.ToInt64(command.ExecuteScalar()) == 0;
    }

    // ── Write ───────────────────────────────────────────────────────

    public void SaveProfile(ProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        InTransaction((c, t) => WriteProfile(c, t, profile));
    }

    public void SaveGlass(GlassDefinition glass)
    {
        ArgumentNullException.ThrowIfNull(glass);
        InTransaction((c, t) => WriteGlass(c, t, glass));
    }

    public void SaveMaterial(MaterialDefinition material)
    {
        ArgumentNullException.ThrowIfNull(material);
        InTransaction((c, t) => WriteMaterial(c, t, material));
    }

    public void SaveSystem(ProductSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        InTransaction((c, t) => WriteDocument(c, t, "systems", system.Id, system));
    }

    public void SaveBundle(Bundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        InTransaction((c, t) => WriteDocument(c, t, "bundles", bundle.Id, bundle));
    }

    public void SaveAll(ProductLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        InTransaction((c, t) =>
        {
            foreach (var m in library.Materials) WriteMaterial(c, t, m);
            foreach (var p in library.Profiles) WriteProfile(c, t, p);
            foreach (var g in library.Glass) WriteGlass(c, t, g);
            foreach (var x in library.Systems) WriteDocument(c, t, "systems", x.Id, x);
            foreach (var b in library.Bundles) WriteDocument(c, t, "bundles", b.Id, b);
            WriteSettings(c, t, library.Currency, library.Defaults);
        });
    }

    public void Delete(LibraryItemKind kind, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        string table = kind switch
        {
            LibraryItemKind.Profile => "profiles",
            LibraryItemKind.Glass => "glass",
            LibraryItemKind.System => "systems",
            LibraryItemKind.Bundle => "bundles",
            _ => "materials"
        };
        InTransaction((c, t) =>
        {
            if (Run(c, t, $"DELETE FROM {table} WHERE id = $id", ("$id", id)) == 0)
                throw new DataStoreException($"The {kind.ToString().ToLowerInvariant()} '{id}' is not in the library.");
        });
    }

    public void SaveSettings(string currency, LibraryDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        InTransaction((c, t) => WriteSettings(c, t, currency, defaults));
    }

    public void Insert(IReadOnlyList<MaterialDefinition> materials, IReadOnlyList<ProfileDefinition> profiles,
        IReadOnlyList<GlassDefinition> glass, (string Currency, LibraryDefaults Defaults)? settings,
        IReadOnlyList<ProductSystem>? systems = null, IReadOnlyList<Bundle>? bundles = null)
    {
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(glass);
        InTransaction((c, t) =>
        {
            foreach (var m in materials) WriteMaterial(c, t, m);
            foreach (var p in profiles) WriteProfile(c, t, p);
            foreach (var g in glass) WriteGlass(c, t, g);
            foreach (var x in systems ?? Array.Empty<ProductSystem>()) WriteDocument(c, t, "systems", x.Id, x);
            foreach (var b in bundles ?? Array.Empty<Bundle>()) WriteDocument(c, t, "bundles", b.Id, b);
            if (settings is { } s) WriteSettings(c, t, s.Currency, s.Defaults);
        });
    }

    /// <summary>A system or bundle row: its definition as JSON, keeping its place in the library order.</summary>
    private static void WriteDocument<T>(SqliteConnection c, SqliteTransaction t, string table, string id, T definition)
        => Run(c, t, $"""
            INSERT INTO {table} (id, sort_order, definition_json)
            VALUES ($id, (SELECT COALESCE(MAX(sort_order), 0) + 1 FROM {table}), $json)
            ON CONFLICT (id) DO UPDATE SET definition_json = excluded.definition_json
            """, ("$id", id), ("$json", JsonSerializer.Serialize(definition, DocumentOptions)));

    private static string? DocumentJson<T>(T? value) where T : class
        => value is null ? null : JsonSerializer.Serialize(value, DocumentOptions);

    private static T? Document<T>(SqliteDataReader r, int ordinal) where T : class
        => r.IsDBNull(ordinal) ? null : JsonSerializer.Deserialize<T>(r.GetString(ordinal), DocumentOptions);

    private static void WriteMaterial(SqliteConnection c, SqliteTransaction t, MaterialDefinition m)
    {
        Run(c, t, """
            INSERT INTO materials (id, sort_order, name, code, manufacturer, category, unit, cost_per_unit, properties_json, is_active,
                                   used_with_json)
            VALUES ($id, (SELECT COALESCE(MAX(sort_order), 0) + 1 FROM materials), $name, $code, $manufacturer, $category,
                    $unit, $cost, $properties, $active, $usedWith)
            ON CONFLICT (id) DO UPDATE SET name = excluded.name, code = excluded.code, manufacturer = excluded.manufacturer,
                category = excluded.category, unit = excluded.unit, cost_per_unit = excluded.cost_per_unit,
                properties_json = excluded.properties_json, is_active = excluded.is_active, used_with_json = excluded.used_with_json
            """,
            ("$id", m.Id), ("$name", m.Name), ("$code", m.Code), ("$manufacturer", m.Manufacturer),
            ("$category", m.Category.ToString()), ("$unit", m.Unit.ToString()), ("$cost", Money(m.CostPerUnit)),
            ("$properties", WriteProperties(m.Properties)), ("$active", m.IsActive ? 1 : 0), ("$usedWith", DocumentJson(m.UsedWith)));
    }

    private static void WriteProfile(SqliteConnection c, SqliteTransaction t, ProfileDefinition p)
    {
        Run(c, t, """
            INSERT INTO profiles (id, sort_order, name, code, manufacturer, series, face_width_mm, depth_mm, weight_kg_per_m,
                                  cost_per_m, stock_length_mm, cut_allowance_per_end_mm, glazing_bite_mm, properties_json, is_active,
                                  used_with_json, reinforcement_json)
            VALUES ($id, (SELECT COALESCE(MAX(sort_order), 0) + 1 FROM profiles), $name, $code, $manufacturer, $series, $face,
                    $depth, $weight, $cost, $stock, $allowance, $bite, $properties, $active, $usedWith, $reinforcement)
            ON CONFLICT (id) DO UPDATE SET name = excluded.name, code = excluded.code, manufacturer = excluded.manufacturer,
                series = excluded.series, face_width_mm = excluded.face_width_mm, depth_mm = excluded.depth_mm,
                weight_kg_per_m = excluded.weight_kg_per_m, cost_per_m = excluded.cost_per_m,
                stock_length_mm = excluded.stock_length_mm, cut_allowance_per_end_mm = excluded.cut_allowance_per_end_mm,
                glazing_bite_mm = excluded.glazing_bite_mm, properties_json = excluded.properties_json, is_active = excluded.is_active,
                used_with_json = excluded.used_with_json, reinforcement_json = excluded.reinforcement_json
            """,
            ("$usedWith", DocumentJson(p.UsedWith)), ("$reinforcement", DocumentJson(p.Reinforcement)),
            ("$id", p.Id), ("$name", p.Name), ("$code", p.Code), ("$manufacturer", p.Manufacturer), ("$series", p.Series),
            ("$face", p.FaceWidthMm), ("$depth", p.DepthMm), ("$weight", p.WeightKgPerMetre), ("$cost", Money(p.CostPerMetre)),
            ("$stock", p.StockLengthMm), ("$allowance", p.CutAllowancePerEndMm), ("$bite", p.GlazingBiteMm),
            ("$properties", WriteProperties(p.Properties)), ("$active", p.IsActive ? 1 : 0));

        Run(c, t, "DELETE FROM profile_roles WHERE profile_id = $id", ("$id", p.Id));
        Run(c, t, "DELETE FROM profile_stock_lengths WHERE profile_id = $id", ("$id", p.Id));
        Run(c, t, "DELETE FROM profile_material_usages WHERE profile_id = $id", ("$id", p.Id));
        for (int i = 0; i < p.Roles.Count; i++)
            Run(c, t, "INSERT INTO profile_roles (profile_id, position, role) VALUES ($id, $pos, $role)",
                ("$id", p.Id), ("$pos", i), ("$role", p.Roles[i].ToString()));
        for (int i = 0; i < p.StockLengthsMm.Count; i++)
            Run(c, t, "INSERT INTO profile_stock_lengths (profile_id, position, length_mm) VALUES ($id, $pos, $length)",
                ("$id", p.Id), ("$pos", i), ("$length", p.StockLengthsMm[i]));
        for (int i = 0; i < p.Materials.Count; i++)
            WriteUsage(c, t, "profile_material_usages", "profile_id", p.Id, i, p.Materials[i]);
    }

    private static void WriteGlass(SqliteConnection c, SqliteTransaction t, GlassDefinition g)
    {
        Run(c, t, """
            INSERT INTO glass (id, sort_order, name, code, manufacturer, category, thickness_mm, cost_per_m2, weight_kg_per_m2,
                               min_chargeable_area_m2, properties_json, is_active, used_with_json)
            VALUES ($id, (SELECT COALESCE(MAX(sort_order), 0) + 1 FROM glass), $name, $code, $manufacturer, $category,
                    $thickness, $cost, $weight, $minArea, $properties, $active, $usedWith)
            ON CONFLICT (id) DO UPDATE SET name = excluded.name, code = excluded.code, manufacturer = excluded.manufacturer,
                category = excluded.category, thickness_mm = excluded.thickness_mm, cost_per_m2 = excluded.cost_per_m2,
                weight_kg_per_m2 = excluded.weight_kg_per_m2, min_chargeable_area_m2 = excluded.min_chargeable_area_m2,
                properties_json = excluded.properties_json, is_active = excluded.is_active, used_with_json = excluded.used_with_json
            """,
            ("$usedWith", DocumentJson(g.UsedWith)),
            ("$id", g.Id), ("$name", g.Name), ("$code", g.Code), ("$manufacturer", g.Manufacturer), ("$category", g.Category),
            ("$thickness", g.ThicknessMm), ("$cost", Money(g.CostPerSquareMetre)), ("$weight", g.WeightKgPerSquareMetre),
            ("$minArea", g.MinChargeableAreaM2), ("$properties", WriteProperties(g.Properties)), ("$active", g.IsActive ? 1 : 0));

        Run(c, t, "DELETE FROM glass_material_usages WHERE glass_id = $id", ("$id", g.Id));
        for (int i = 0; i < g.Materials.Count; i++)
            WriteUsage(c, t, "glass_material_usages", "glass_id", g.Id, i, g.Materials[i]);
    }

    private static void WriteUsage(SqliteConnection c, SqliteTransaction t, string table, string ownerColumn, string ownerId,
        int position, MaterialUsage usage)
        => Run(c, t, $"INSERT INTO {table} ({ownerColumn}, position, material_id, basis, quantity) VALUES ($owner, $pos, $material, $basis, $qty)",
            ("$owner", ownerId), ("$pos", position), ("$material", usage.MaterialId), ("$basis", usage.Basis.ToString()),
            ("$qty", usage.Quantity));

    private static void WriteSettings(SqliteConnection c, SqliteTransaction t, string currency, LibraryDefaults d)
        => Run(c, t, """
            UPDATE library_settings SET currency = $currency, default_frame_profile_id = $frame,
                default_mullion_profile_id = $mullion, default_transom_profile_id = $transom, default_glass_id = $glass,
                default_system_id = $system
            WHERE id = 1
            """,
            ("$currency", currency ?? ""), ("$frame", d.FrameProfileId), ("$mullion", d.MullionProfileId),
            ("$transom", d.TransomProfileId), ("$glass", d.GlassId), ("$system", d.SystemId));

    // ── Helpers ─────────────────────────────────────────────────────

    /// <summary>Runs <paramref name="work"/> in one transaction; any SQLite failure (including opening) rolls back and is reported.</summary>
    private void InTransaction(Action<SqliteConnection, SqliteTransaction> work)
        => _database.Guard("save the library change", () =>
        {
            using var connection = _database.Connect();
            using var transaction = connection.BeginTransaction();
            work(connection, transaction);
            transaction.Commit();
        });

    private static int Run(SqliteConnection c, SqliteTransaction t, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = c.CreateCommand();
        command.Transaction = t;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command.ExecuteNonQuery();
    }

    private static (string Currency, LibraryDefaults Defaults) ReadSettings(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT currency, default_frame_profile_id, default_mullion_profile_id, default_transom_profile_id, " +
                              "default_glass_id, default_system_id FROM library_settings WHERE id = 1";
        using var r = command.ExecuteReader();
        if (!r.Read())
            throw new DataStoreException("The database has no library settings row.");
        return (r.GetString(0), new LibraryDefaults
        {
            FrameProfileId = NullableString(r, 1),
            MullionProfileId = NullableString(r, 2),
            TransomProfileId = NullableString(r, 3),
            GlassId = NullableString(r, 4),
            SystemId = NullableString(r, 5)
        });
    }

    private static List<T> ReadRows<T>(SqliteConnection connection, string sql, Func<SqliteDataReader, T> map)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read())
            rows.Add(map(reader));
        return rows;
    }

    /// <summary>Child rows grouped by owner id (column 0), in the query's order.</summary>
    private static Dictionary<string, List<T>> ReadChildren<T>(SqliteConnection connection, string sql, Func<SqliteDataReader, T> map)
    {
        var result = new Dictionary<string, List<T>>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string owner = reader.GetString(0);
            if (!result.TryGetValue(owner, out var list))
                result[owner] = list = new List<T>();
            list.Add(map(reader));
        }
        return result;
    }

    private static IReadOnlyList<T> ChildrenOf<T>(Dictionary<string, List<T>> children, string owner)
        => children.TryGetValue(owner, out var list) ? list.AsReadOnly() : Array.Empty<T>();

    private static MaterialUsage ReadUsage(SqliteDataReader r) => new()
    {
        MaterialId = r.GetString(1),
        Basis = Enum.Parse<UsageBasis>(r.GetString(2)),
        Quantity = r.GetDouble(3)
    };

    private static string? NullableString(SqliteDataReader r, int ordinal) => r.IsDBNull(ordinal) ? null : r.GetString(ordinal);

    private static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static decimal ParseMoney(string text) => decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);

    /// <summary>Properties as JSON with keys in ordinal order, so equal dictionaries are stored identically.</summary>
    private static string WriteProperties(IReadOnlyDictionary<string, string>? properties)
        => JsonSerializer.Serialize(new SortedDictionary<string, string>(
            (properties ?? new Dictionary<string, string>()).ToDictionary(p => p.Key, p => p.Value), StringComparer.Ordinal));

    private static IReadOnlyDictionary<string, string> ReadProperties(string json)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
}
