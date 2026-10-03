using System.Text;
using System.Text.Json;
using Mark.Licensing;
using Xunit;

namespace Mark.Tests.Licensing;

/// <summary>The licence format: signatures, the evaluation of validity, grace and clock, passwords, the catalogue.</summary>
public class LicenceTests
{
    private static readonly DateTime Now = TestClock.Start;

    private static Licence Sample(Action<LicenceBuilder>? change = null)
    {
        var builder = new LicenceBuilder();
        change?.Invoke(builder);
        return builder.Build();
    }

    private sealed class LicenceBuilder
    {
        public DateTime Issued = Now;
        public DateTime ValidUntil = Now.AddDays(100);
        public bool Suspended;
        public List<ProductGrant> Products = new() { new ProductGrant(Product.Upvc, Now.AddDays(100)) };
        public List<FeatureGrant> Grants = new()
        {
            new FeatureGrant(Mark.Licensing.Features.Quotes, Now.AddDays(100)),
            new FeatureGrant(Mark.Licensing.Features.Drawing, Now.AddDays(100)),
            new FeatureGrant(Mark.Licensing.Features.Openings, Now.AddDays(100)),
            new FeatureGrant(Mark.Licensing.Features.CuttingPlans, Now.AddDays(10))
        };

        public Licence Build() => new()
        {
            LicenceId = Guid.NewGuid(), CompanyId = Guid.NewGuid(), CompanyName = "Shree Windows", UserId = "shree", UserName = "Ravi",
            MachineId = "PC-1", IssuedUtc = Issued, ValidUntilUtc = ValidUntil, Suspended = Suspended, PackageName = "Basic",
            MaxComputers = 2, Products = Products, Features = Grants
        };
    }

    // ── Signature ───────────────────────────────────────────────────

    [Fact]
    public void SignedLicence_VerifiesAndReadsBack()
    {
        using var signer = LicenceSigner.CreateNew();
        var licence = Sample();

        var read = new LicenceVerifier(signer.PublicKey).Verify(signer.Sign(licence), out string? error);

        Assert.Null(error);
        Assert.NotNull(read);
        Assert.Equal(licence.CompanyName, read!.CompanyName);
        Assert.Equal(licence.ValidUntilUtc, read.ValidUntilUtc);
        Assert.Equal(licence.Features, read.Features);
        Assert.Equal(licence.Products, read.Products);
    }

    [Fact]
    public void ChangedLicence_IsRejected()
    {
        using var signer = LicenceSigner.CreateNew();
        var signed = signer.Sign(Sample());
        // Someone edits the payload to extend the validity by a year.
        string json = Encoding.UTF8.GetString(Convert.FromBase64String(signed.Payload));
        var edited = JsonSerializer.Deserialize<Licence>(json, LicenceJson.Options)! with { ValidUntilUtc = Now.AddYears(5) };
        var tampered = signed with { Payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(edited, LicenceJson.Options)) };

        Assert.Null(new LicenceVerifier(signer.PublicKey).Verify(tampered, out string? error));
        Assert.Contains("changed", error);
    }

    [Fact]
    public void LicenceSignedWithAnotherKey_IsRejected()
    {
        using var server = LicenceSigner.CreateNew();
        using var forger = LicenceSigner.CreateNew();

        Assert.Null(new LicenceVerifier(server.PublicKey).Verify(forger.Sign(Sample()), out _));
    }

    [Fact]
    public void DamagedLicence_IsRejectedWithoutThrowing()
    {
        using var signer = LicenceSigner.CreateNew();
        var verifier = new LicenceVerifier(signer.PublicKey);

        Assert.Null(verifier.Verify(new SignedLicence("not base64!", "x"), out string? error));
        Assert.Equal("The licence is damaged.", error);
        Assert.Null(verifier.Verify(null, out _));
    }

    [Fact]
    public void SigningKey_SurvivesPemRoundTrip()
    {
        using var signer = LicenceSigner.CreateNew();
        using var reloaded = LicenceSigner.FromPem(signer.ExportPrivateKeyPem());

        Assert.Equal(signer.PublicKey, reloaded.PublicKey);
        Assert.NotNull(new LicenceVerifier(signer.PublicKey).Verify(reloaded.Sign(Sample()), out _));
    }

    [Fact]
    public void MarkPublicKey_IsAValidKey()
    {
        // MARK is built with the real server's public key; it must at least load.
        var verifier = LicenceVerifier.ForMark();
        using var other = LicenceSigner.CreateNew();
        Assert.Null(verifier.Verify(other.Sign(Sample()), out string? error));
        Assert.Contains("not issued", error);
    }

    // ── Evaluation ──────────────────────────────────────────────────

    [Fact]
    public void ValidLicence_IsFullWithItsFeaturesAndProducts()
    {
        var status = LicenceEvaluator.Evaluate(Sample(), Now.AddHours(1));

        Assert.Equal(LicenceMode.Full, status.Mode);
        Assert.Null(status.Reason);
        Assert.True(status.Allows(Features.Openings));
        Assert.False(status.Allows(Features.PriceStructure));
        Assert.Equal(new[] { Product.Upvc }, status.Products);
    }

    [Fact]
    public void AddOnPastItsDate_IsLockedWhileTheRestWorks()
    {
        // Cutting plans were added on for 10 days; within the 7-day grace after a check-in on day 9.
        var licence = Sample(b => b.Issued = Now.AddDays(9));
        var status = LicenceEvaluator.Evaluate(licence, Now.AddDays(11));

        Assert.Equal(LicenceMode.Full, status.Mode);
        Assert.False(status.Allows(Features.CuttingPlans));
        Assert.True(status.Allows(Features.Openings));
    }

    [Fact]
    public void ExpiredAccount_IsReadOnly_ButShowsEverythingForViewing()
    {
        var licence = Sample(b =>
        {
            b.ValidUntil = Now.AddDays(1);
            b.Issued = Now.AddDays(1);
        });
        var status = LicenceEvaluator.Evaluate(licence, Now.AddDays(2));

        Assert.True(status.IsReadOnly);
        Assert.Contains("ended on", status.Reason);
        Assert.True(status.Allows(Features.Openings));
    }

    [Fact]
    public void SuspendedAccount_IsReadOnly()
    {
        var status = LicenceEvaluator.Evaluate(Sample(b => b.Suspended = true), Now);

        Assert.True(status.IsReadOnly);
        Assert.Contains("suspended", status.Reason);
    }

    [Fact]
    public void NoValidProduct_IsReadOnly()
    {
        var licence = Sample(b => b.Products = new() { new ProductGrant(Product.Aluminium, Now.AddDays(-1)) });

        var status = LicenceEvaluator.Evaluate(licence, Now);

        Assert.True(status.IsReadOnly);
        Assert.Contains("Aluminium", status.Reason);
    }

    [Fact]
    public void OfflineLongerThanGrace_IsReadOnly_AndWarnedBefore()
    {
        var licence = Sample();

        var warned = LicenceEvaluator.Evaluate(licence, Now.AddDays(5));
        Assert.Equal(LicenceMode.Full, warned.Mode);
        Assert.Contains("not reached the licence server for 5 days", warned.Warning);

        var late = LicenceEvaluator.Evaluate(licence, Now.AddDays(7.5));
        Assert.True(late.IsReadOnly);
        Assert.Contains("7 days", late.Reason);
    }

    [Fact]
    public void ClockMovedBack_IsReadOnly()
    {
        var licence = Sample();

        Assert.True(LicenceEvaluator.Evaluate(licence, Now.AddDays(-1)).IsReadOnly);
        var status = LicenceEvaluator.Evaluate(licence, Now.AddDays(1), lastSeenUtc: Now.AddDays(3));
        Assert.True(status.IsReadOnly);
        Assert.Contains("date on this computer", status.Reason);
        // A small difference (time zones, clock sync) is fine.
        Assert.False(LicenceEvaluator.Evaluate(licence, Now.AddHours(1), lastSeenUtc: Now.AddHours(2)).IsReadOnly);
    }

    [Fact]
    public void LicenceEndingSoon_IsWarned()
    {
        var licence = Sample(b => b.ValidUntil = Now.AddDays(9).AddHours(1));

        var status = LicenceEvaluator.Evaluate(licence, Now);

        Assert.Equal(LicenceMode.Full, status.Mode);
        Assert.Contains("(10 days)", status.Warning);
    }

    // ── Passwords, keys, catalogue ──────────────────────────────────

    [Fact]
    public void PasswordHash_VerifiesOnlyTheRightPassword()
    {
        string hash = PasswordHasher.Hash("secret1", iterations: 1000);

        Assert.True(PasswordHasher.Verify("secret1", hash));
        Assert.False(PasswordHasher.Verify("Secret1", hash));
        Assert.False(PasswordHasher.Verify("secret1", "garbage"));
        Assert.NotEqual(hash, PasswordHasher.Hash("secret1", iterations: 1000));   // salted
        Assert.NotNull(PasswordHasher.Check("12345"));
        Assert.Null(PasswordHasher.Check("123456"));
    }

    [Fact]
    public void LicenceKeys_AreReadableAndNormalised()
    {
        string key = Secrets.NewLicenceKey();

        Assert.Matches("^MARK(-[A-HJ-NP-Z2-9]{5}){4}$", key);
        Assert.Equal(key, Secrets.NormaliseKey(" " + key.ToLowerInvariant().Replace("-", "- ") + " "));
        Assert.NotEqual(key, Secrets.NewLicenceKey());
    }

    [Fact]
    public void FeatureCatalogue_IsConsistent()
    {
        var ids = FeatureCatalog.All.Select(f => f.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(FeatureCatalog.All, f => Assert.Contains(f.Area, FeatureCatalog.Areas));
        Assert.Equal(new[] { Features.Quotes, Features.Drawing }, FeatureCatalog.CoreIds);
        // Every feature MARK gates in code is in the catalogue.
        foreach (var field in typeof(Features).GetFields())
            Assert.True(FeatureCatalog.Exists((string)field.GetValue(null)!), field.Name);
    }

    [Fact]
    public void StarterPackages_GrowFromBasicToComplete()
    {
        var packages = StarterPackages.All.ToDictionary(p => p.Name, p => p.Features);

        Assert.Subset(packages["Professional"].ToHashSet(), packages["Basic"].ToHashSet());
        Assert.Subset(packages["Complete"].ToHashSet(), packages["Professional"].ToHashSet());
        Assert.Equal(FeatureCatalog.All.Count, packages["Complete"].Count);
        Assert.All(packages.Values, features => Assert.Subset(features.ToHashSet(), FeatureCatalog.CoreIds.ToHashSet()));
    }
}
