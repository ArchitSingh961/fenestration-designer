namespace Mark.Data;

/// <summary>
/// The SQLite schema and its versions. The version is stored in <c>PRAGMA user_version</c>; the file is marked as ours
/// with <c>PRAGMA application_id</c> so a foreign SQLite file is never mistaken for a MARK database.
///
/// To change the schema: add the upgrade script for (Current → Current + 1) to <see cref="Upgrades"/>, then increment
/// <see cref="CurrentVersion"/>. Upgrades run in order, each in its own transaction, and never drop user data.
/// </summary>
internal static class DatabaseSchema
{
    /// <summary>"FEN1" — identifies a MARK database file (value kept from before the rename, so existing databases still open).</summary>
    public const int ApplicationId = 0x46454E31;

    /// <summary>The schema version written by this build.</summary>
    public const int CurrentVersion = 6;

    /// <summary>Upgrade scripts keyed by the version they upgrade FROM.</summary>
    public static readonly IReadOnlyDictionary<int, string> Upgrades = new Dictionary<int, string>
    {
        [1] = Version2,
        [2] = Version3,
        [3] = Version4,
        [4] = Version5,
        [5] = Version6
    };

    /// <summary>Version 6: how each glass is drawn (its pattern and colour), as JSON.</summary>
    public const string Version6 = """
        ALTER TABLE glass ADD COLUMN look_json TEXT NULL;
        """;

    /// <summary>
    /// Version 5 (Milestone 14, who did what): who created and last saved each quote, and the history of every quote
    /// (created, saved with what changed, deleted). History has no foreign key, so it outlives a deleted quote.
    /// </summary>
    public const string Version5 = """
        ALTER TABLE projects ADD COLUMN created_by  TEXT NOT NULL DEFAULT '';
        ALTER TABLE projects ADD COLUMN modified_by TEXT NOT NULL DEFAULT '';
        CREATE TABLE project_history (
            id            INTEGER PRIMARY KEY,
            project_id    TEXT NOT NULL,
            quote_number  TEXT NOT NULL,
            project_name  TEXT NOT NULL,
            time_utc      TEXT NOT NULL,
            user_id       TEXT NOT NULL,
            user_name     TEXT NOT NULL,
            action        TEXT NOT NULL,
            detail        TEXT NOT NULL
        );
        CREATE INDEX ix_project_history_project ON project_history (project_id, time_utc);
        CREATE INDEX ix_project_history_time ON project_history (time_utc);
        """;

    /// <summary>
    /// Version 4 (Milestone 13, product systems): systems and bundles as JSON documents in library order; what each
    /// item is used with, and each profile's reinforcement, as JSON columns; the default system.
    /// </summary>
    public const string Version4 = """
        ALTER TABLE profiles ADD COLUMN used_with_json TEXT NULL;
        ALTER TABLE profiles ADD COLUMN reinforcement_json TEXT NULL;
        ALTER TABLE glass ADD COLUMN used_with_json TEXT NULL;
        ALTER TABLE materials ADD COLUMN used_with_json TEXT NULL;
        ALTER TABLE library_settings ADD COLUMN default_system_id TEXT NULL;
        CREATE TABLE systems (
            id              TEXT PRIMARY KEY NOT NULL,
            sort_order      INTEGER NOT NULL,
            definition_json TEXT NOT NULL
        );
        CREATE TABLE bundles (
            id              TEXT PRIMARY KEY NOT NULL,
            sort_order      INTEGER NOT NULL,
            definition_json TEXT NOT NULL
        );
        """;

    /// <summary>Version 3 (Milestone 11, pricing): company settings such as the default price structure, as JSON by key.</summary>
    public const string Version3 = """
        CREATE TABLE app_settings (
            key         TEXT PRIMARY KEY NOT NULL,
            value_json  TEXT NOT NULL
        );
        """;

    /// <summary>
    /// Version 2 (Milestone 10, quotes): the quote list reads number, client, status and totals without loading every
    /// document. These columns are a summary of the document, rewritten on every save; the document stays the source of
    /// truth. Rows saved before this version are filled in when the store opens (<c>BackfillQuoteSummaries</c>).
    /// </summary>
    public const string Version2 = """
        ALTER TABLE projects ADD COLUMN quote_number  TEXT NOT NULL DEFAULT '';
        ALTER TABLE projects ADD COLUMN client_name   TEXT NOT NULL DEFAULT '';
        ALTER TABLE projects ADD COLUMN status        TEXT NOT NULL DEFAULT 'Active';
        ALTER TABLE projects ADD COLUMN design_count  INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE projects ADD COLUMN quantity      INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE projects ADD COLUMN area_m2       REAL NOT NULL DEFAULT 0;
        ALTER TABLE projects ADD COLUMN value         TEXT NULL;
        ALTER TABLE projects ADD COLUMN currency      TEXT NOT NULL DEFAULT '';
        ALTER TABLE projects ADD COLUMN summary_version INTEGER NOT NULL DEFAULT 0;
        CREATE INDEX ix_projects_status ON projects (status);
        CREATE INDEX ix_projects_quote_number ON projects (quote_number);
        """;

    /// <summary>
    /// Version 1. Products keep the library's string ids as primary keys and an explicit <c>sort_order</c> (library
    /// order), so reads are deterministic. Prices are exact decimals stored as invariant text. A project is stored as
    /// its versioned JSON document (the same format as a project file); <c>project_references</c> records which library
    /// products it uses, so a referenced product is never deleted silently. Project → product references are not
    /// foreign keys on purpose: a project may reference a product this library does not have (reported, not rejected).
    /// </summary>
    public const string Version1 = """
        CREATE TABLE materials (
            id               TEXT PRIMARY KEY NOT NULL,
            sort_order       INTEGER NOT NULL,
            name             TEXT NOT NULL,
            code             TEXT NULL,
            manufacturer     TEXT NULL,
            category         TEXT NOT NULL,
            unit             TEXT NOT NULL,
            cost_per_unit    TEXT NOT NULL,
            properties_json  TEXT NOT NULL DEFAULT '{}',
            is_active        INTEGER NOT NULL DEFAULT 1
        );

        CREATE TABLE profiles (
            id                        TEXT PRIMARY KEY NOT NULL,
            sort_order                INTEGER NOT NULL,
            name                      TEXT NOT NULL,
            code                      TEXT NULL,
            manufacturer              TEXT NULL,
            series                    TEXT NULL,
            face_width_mm             REAL NOT NULL,
            depth_mm                  REAL NOT NULL,
            weight_kg_per_m           REAL NOT NULL,
            cost_per_m                TEXT NOT NULL,
            stock_length_mm           REAL NOT NULL,
            cut_allowance_per_end_mm  REAL NOT NULL,
            glazing_bite_mm           REAL NOT NULL,
            properties_json           TEXT NOT NULL DEFAULT '{}',
            is_active                 INTEGER NOT NULL DEFAULT 1
        );

        CREATE TABLE profile_roles (
            profile_id  TEXT NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
            position    INTEGER NOT NULL,
            role        TEXT NOT NULL,
            PRIMARY KEY (profile_id, position)
        );

        CREATE TABLE profile_stock_lengths (
            profile_id  TEXT NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
            position    INTEGER NOT NULL,
            length_mm   REAL NOT NULL,
            PRIMARY KEY (profile_id, position)
        );

        CREATE TABLE profile_material_usages (
            profile_id   TEXT NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
            position     INTEGER NOT NULL,
            material_id  TEXT NOT NULL REFERENCES materials(id),
            basis        TEXT NOT NULL,
            quantity     REAL NOT NULL,
            PRIMARY KEY (profile_id, position)
        );

        CREATE TABLE glass (
            id                        TEXT PRIMARY KEY NOT NULL,
            sort_order                INTEGER NOT NULL,
            name                      TEXT NOT NULL,
            code                      TEXT NULL,
            manufacturer              TEXT NULL,
            category                  TEXT NULL,
            thickness_mm              REAL NOT NULL,
            cost_per_m2               TEXT NOT NULL,
            weight_kg_per_m2          REAL NULL,
            min_chargeable_area_m2    REAL NOT NULL,
            properties_json           TEXT NOT NULL DEFAULT '{}',
            is_active                 INTEGER NOT NULL DEFAULT 1
        );

        CREATE TABLE glass_material_usages (
            glass_id     TEXT NOT NULL REFERENCES glass(id) ON DELETE CASCADE,
            position     INTEGER NOT NULL,
            material_id  TEXT NOT NULL REFERENCES materials(id),
            basis        TEXT NOT NULL,
            quantity     REAL NOT NULL,
            PRIMARY KEY (glass_id, position)
        );

        -- After the product tables: the defaults reference them, and SQLite checks referenced tables on insert.
        CREATE TABLE library_settings (
            id                          INTEGER PRIMARY KEY CHECK (id = 1),
            currency                    TEXT NOT NULL DEFAULT '',
            default_frame_profile_id    TEXT NULL REFERENCES profiles(id),
            default_mullion_profile_id  TEXT NULL REFERENCES profiles(id),
            default_transom_profile_id  TEXT NULL REFERENCES profiles(id),
            default_glass_id            TEXT NULL REFERENCES glass(id)
        );
        INSERT INTO library_settings (id) VALUES (1);

        CREATE TABLE projects (
            id              TEXT PRIMARY KEY NOT NULL,
            name            TEXT NOT NULL,
            format_version  INTEGER NOT NULL,
            document_json   TEXT NOT NULL,
            created_utc     TEXT NOT NULL,
            modified_utc    TEXT NOT NULL
        );

        CREATE TABLE project_references (
            project_id     TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
            kind           TEXT NOT NULL,
            definition_id  TEXT NOT NULL,
            PRIMARY KEY (project_id, kind, definition_id)
        );

        CREATE INDEX ix_project_references_definition ON project_references (kind, definition_id);
        CREATE INDEX ix_projects_modified ON projects (modified_utc);
        CREATE INDEX ix_profiles_name ON profiles (name);
        CREATE INDEX ix_profiles_manufacturer ON profiles (manufacturer);
        CREATE INDEX ix_profiles_series ON profiles (series);
        CREATE INDEX ix_glass_name ON glass (name);
        CREATE INDEX ix_glass_manufacturer ON glass (manufacturer);
        CREATE INDEX ix_glass_category ON glass (category);
        CREATE INDEX ix_materials_name ON materials (name);
        CREATE INDEX ix_materials_manufacturer ON materials (manufacturer);
        CREATE INDEX ix_materials_category ON materials (category);
        CREATE INDEX ix_profile_usages_material ON profile_material_usages (material_id);
        CREATE INDEX ix_glass_usages_material ON glass_material_usages (material_id);
        """;
}
