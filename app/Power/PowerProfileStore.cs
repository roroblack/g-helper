using System.Text.Json;
using System.Text.Json.Serialization;

namespace GHelper.Power;

// These are user-authored drafts, NOT vendor defaults, hardware measurements,
// safe limits, active settings or an authorization to write firmware.
internal sealed record PowerProfileDraft(int Slot, bool AC, int PL1, int PL2);
internal sealed record PowerApplyCheck(bool Applied, bool EffectiveVerified, string Reason)
{
    public IReadOnlyList<PowerCapabilityBlocker> Blockers => N1xPowerCapability.MissingEvidence;
}
internal sealed class PowerProfileDocument
{
    public int Schema { get; set; }
    public Dictionary<string, PowerProfileDraft> Profiles { get; set; } = new();
}

internal sealed class PowerProfileStore
{
    private readonly string path;
    // All instances share serialization: prevents read-modify-write lost updates
    // when two editor windows save different AC/DC or custom slots.
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public PowerProfileStore(string path) => this.path = Path.GetFullPath(path);
    private static void ValidateSlot(int slot)
    {
        if (slot != 20 && (slot < 3 || slot > 19))
            throw new ArgumentException("수동20 또는 커스텀3..19만 편집할 수 있습니다.");
    }
    private static string Key(int slot, bool ac) => $"{slot}:{(ac ? "AC" : "DC")}";
    private static void Validate(PowerProfileDraft p)
    {
        ValidateSlot(p.Slot);
        // Structural validity only. Do NOT use this as model-specific safe bounds.
        if (p.PL1 <= 0 || p.PL2 <= 0 || p.PL1 > p.PL2)
            throw new ArgumentException("초안은 0 < PL1 <= PL2 정수여야 합니다. 안전한 하드웨어 범위를 뜻하지 않습니다.");
    }
    private PowerProfileDocument Read()
    {
        if (!File.Exists(path)) return new PowerProfileDocument { Schema = 1 };
        var doc = JsonSerializer.Deserialize<PowerProfileDocument>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("프로필 문서가 null입니다.");
        if (doc.Schema != 1 || doc.Profiles == null) throw new InvalidDataException("지원하지 않는 프로필 형식입니다.");
        foreach (var entry in doc.Profiles)
        {
            if (entry.Value == null) throw new InvalidDataException("프로필 항목이 null입니다.");
            Validate(entry.Value);
            if (entry.Key != Key(entry.Value.Slot, entry.Value.AC)) throw new InvalidDataException("프로필 슬롯/전원 키가 다릅니다.");
        }
        return doc;
    }
    private void Write(PowerProfileDocument doc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, doc, Json);
                stream.Flush(flushToDisk: true);
            }
            // Atomically replace the destination, never truncate the previous draft.
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public PowerProfileDraft? Load(int slot, bool ac)
    {
        ValidateSlot(slot);
        lock (Gate) return Read().Profiles.GetValueOrDefault(Key(slot, ac));
    }
    public void Save(int slot, bool ac, int pl1, int pl2)
    {
        var draft = new PowerProfileDraft(slot, ac, pl1, pl2);
        Validate(draft);
        lock (Gate)
        {
            var doc = Read();
            doc.Profiles[Key(slot, ac)] = draft;
            Write(doc);
        }
    }
    public bool RevertDraft(int slot, bool ac)
    {
        ValidateSlot(slot);
        lock (Gate)
        {
            var doc = Read();
            if (!doc.Profiles.Remove(Key(slot, ac))) return false;
            Write(doc);
            return true;
        }
    }
    public PowerApplyCheck CheckApply(int slot, bool ac)
    {
        var draft = Load(slot, ac);
        return new(false, false, draft == null ? "저장된 초안이 없습니다. 하드웨어 변경 없음." :
            N1xPowerCapability.DescribeBlockers());
    }
}
