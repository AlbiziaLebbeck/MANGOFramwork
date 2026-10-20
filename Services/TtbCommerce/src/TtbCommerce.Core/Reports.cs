namespace TtbCommerce.Core;

public enum ReportLayout { Unknown, CrossBorder301, Legacy238 }
public enum ReportWidth { Unknown, Utf8Bytes, UnicodeScalars }
public enum ReportCreditTotal { Unknown, IncludesVoidedOriginals, ExcludesVoidedOriginals }
public enum ReportKind { Iso, Lost }
public enum ReportDailyScope { Unknown, CalendarDay, BankingDay2256 }

public sealed class ReportOptions
{
    public bool Enabled { get; set; }
    public bool ProfileApproved { get; set; }
    public bool AcceptanceApproved { get; set; }
    public ReportLayout Layout { get; set; }
    public ReportWidth Width { get; set; }
    public ReportCreditTotal CreditTotal { get; set; }
    public ReportDailyScope DailyScope { get; set; }
    public bool SequenceIncludesTrailer { get; set; } = true;
    public bool FileDateIsHeaderDate { get; set; }
    public string MerchantAccount { get; set; } = "";
    public int Port { get; set; } = 4028;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string HostKeySha256 { get; set; } = "";
    public string RemoteSuffix { get; set; } = "";
    public string PgpPrivateKeyPath { get; set; } = "";
    public string PgpPassphrase { get; set; } = "";
    public string RecipientFingerprint { get; set; } = "";
    public string SigningPublicKeyPath { get; set; } = "";
    public string SigningFingerprint { get; set; } = "";
    // Retired pins are for a matching immutable archive profile only, never newly received files.
    public string[] ArchiveRecipientFingerprints { get; set; } = [];
    public string[] ArchiveSigningFingerprints { get; set; } = [];
    public int MaxFileBytes { get; set; } = 33554432;
    public int LookbackDays { get; set; } = 7;
    public int PollMinutes { get; set; } = 30;
    public int IsoDueHour { get; set; } = 1;
    public int LostDueHour { get; set; } = 20;

    public IReadOnlyList<string> Missing()
    {
        var errors = MissingParser().ToList();
        if (Port is not (4022 or 4028)) errors.Add("Reports.Port");
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password)) errors.Add("Reports.SftpCredentials");
        if (!System.Text.RegularExpressions.Regex.IsMatch(HostKeySha256, "\\ASHA256:[A-Za-z0-9+/]{43}\\z")) errors.Add("Reports.HostKeySha256");
        if (RemoteSuffix is not ("" or ".pgp" or ".gpg" or ".asc")) errors.Add("Reports.RemoteSuffix");
        if (string.IsNullOrEmpty(PgpPrivateKeyPath) || !Fingerprint(RecipientFingerprint)) errors.Add("Reports.PgpRecipient");
        if (SigningPublicKeyPath.Length > 0 ? !Fingerprint(SigningFingerprint) : SigningFingerprint.Length > 0) errors.Add("Reports.PgpSigningProfile");
        errors.AddRange(MissingArchiveTrust());
        return errors;
    }
    public IReadOnlyList<string> MissingArchiveTrust()
    {
        var errors = new List<string>();
        if (!ArchivePins(ArchiveRecipientFingerprints, RecipientFingerprint)) errors.Add("Reports.ArchiveRecipientFingerprints");
        if (!ArchivePins(ArchiveSigningFingerprints, SigningFingerprint) ||
            ArchiveSigningFingerprints is { Length: > 0 } && (SigningFingerprint.Length == 0 || SigningPublicKeyPath.Length == 0))
            errors.Add("Reports.ArchiveSigningFingerprints");
        return errors;
    }
    public IReadOnlyList<string> MissingParser()
    {
        var errors = new List<string>();
        if (!ProfileApproved || !Enum.IsDefined(Layout) || Layout == ReportLayout.Unknown || !Enum.IsDefined(Width) || Width == ReportWidth.Unknown ||
            !Enum.IsDefined(CreditTotal) || CreditTotal == ReportCreditTotal.Unknown || !FileDateIsHeaderDate || !Enum.IsDefined(DailyScope) || DailyScope == ReportDailyScope.Unknown) errors.Add("Reports.ApprovedLayoutEncodingTotalsDates");
        if (MerchantAccount.Length is < 1 or > 20 || MerchantAccount.Any(c => c is < '0' or > '9')) errors.Add("Reports.MerchantAccount");
        if (MaxFileBytes is < 1002 or > 67108864 || LookbackDays is < 1 or > 31 || PollMinutes is < 5 or > 240 || IsoDueHour is < 0 or > 23 || LostDueHour is < 0 or > 23)
            errors.Add("Reports.BoundsSchedule");
        return errors;
    }
    public static bool Fingerprint(string? value) => value is { Length: 40 or 64 } && value.All(char.IsAsciiHexDigit);
    private static bool ArchivePins(string[]? pins, string active) => pins is { Length: <= 8 } && pins.All(Fingerprint) &&
        pins.Distinct(StringComparer.OrdinalIgnoreCase).Count() == pins.Length && !pins.Contains(active, StringComparer.OrdinalIgnoreCase);
}

public sealed record ReportCryptoProfile(string RecipientFingerprint, string SigningFingerprint);

public sealed class ReportArchive : Entity
{
    public CommerceEnvironment Environment { get; set; }
    public string MerchantId { get; set; } = "";
    public string FileName { get; set; } = "";
    public ReportKind Kind { get; set; }
    public DateOnly FileDate { get; set; }
    public int Revision { get; set; }
    public string EncryptedSha256 { get; set; } = "";
    public long EncryptedBytes { get; set; }
    public string FileKey { get; set; } = "";
    public string? PlaintextSha256 { get; set; }
    public Guid? ReplayOfId { get; set; }
    public string ParserProfileJson { get; set; } = "{}";
    public string AppliedProfileJson { get; set; } = "{}";
    public string State { get; set; } = "Archived";
    public string? FailureCode { get; set; }
    public string TotalsJson { get; set; } = "{}";
    public string ComparisonJson { get; set; } = "{}";
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class ReportRow : Entity
{
    public Guid ReportArchiveId { get; set; }
    public int Sequence { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string SemanticHash { get; set; } = "";
    public Guid? BankTransactionId { get; set; }
    public string Outcome { get; set; } = "Pending";
    public DateTimeOffset? AppliedAt { get; set; }
}

public sealed class ReportFetch : Entity
{
    public CommerceEnvironment Environment { get; set; }
    public string MerchantId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string State { get; set; } = "WaitingForFile";
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset NextCheckAt { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public string? FailureCode { get; set; }
    public Guid? ReportArchiveId { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
}

public sealed record ReportDetail(int Sequence, string InstructionId, string OriginalInstructionId, string PayerBank, string PayerAccount,
    string PayerName, string PayerNameThai, string MerchantAccount, DateTimeOffset PaymentAt, string Ref1, string Ref2, string QrId,
    string Teller, string TransactionKind, string LumpsumFlag, long AmountMinor, long FeeMinor, string BillerNo,
    DateTimeOffset? CreditAt, string CrossBorderFlag, string SwitchCode, string CurrencyCode, string SwitchFullName);
public sealed record ReportTotals(long DebitMinor, int DebitCount, long CreditMinor, int CreditCount, long VoidedOriginalMinor, int VoidedOriginalCount);
public sealed record ParsedReport(string MerchantId, DateOnly Date, ReportKind Kind, ReportDetail[] Details, ReportTotals Totals);
public sealed record ReportFileName(string Name, ReportKind Kind, DateOnly Date);
