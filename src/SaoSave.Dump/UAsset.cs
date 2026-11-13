using System.Text;

namespace SaoSave.Dump;

/// <summary>
/// Reads a cooked UE 4.27 package (.uasset + .uexp) far enough to list each export's class and
/// its tagged properties as flattened "path = value" pairs. Text properties give their source
/// (English) string. Only what is needed to find item IDs and names.
/// </summary>
public sealed class UAsset
{
    public sealed class Export
    {
        public string Name = "";
        public string Class = "";
        public List<KeyValuePair<string, string>> Props = new();
    }

    /// <summary>Prefix of a text value that is a string-table reference: "{mark}{table id}|{key}".</summary>
    public const string StringTableMark = "\u0001ST:";

    /// <summary>Key/value entries when this package is a StringTable asset.</summary>
    public Dictionary<string, string> StringTable = new();

    public string[] Names = Array.Empty<string>();
    public List<string> Imports = new();
    public List<Export> Exports = new();

    private byte[] _d = Array.Empty<byte>();
    private int _p;

    public static UAsset Load(string uassetPath)
    {
        var a = new UAsset();
        byte[] h = File.ReadAllBytes(uassetPath);
        string uexpPath = Path.ChangeExtension(uassetPath, ".uexp");
        byte[] x = File.Exists(uexpPath) ? File.ReadAllBytes(uexpPath) : Array.Empty<byte>();
        a.Parse(h, x);
        return a;
    }

    private void Parse(byte[] h, byte[] uexp)
    {
        int p = 0;
        if (BitConverter.ToUInt32(h, 0) != 0x9E2A83C1) throw new InvalidDataException("not a uasset");
        p = 4;
        int legacy = BitConverter.ToInt32(h, p); p += 4;
        if (legacy != -4) p += 4;
        p += 8; // UE4 version, licensee version
        int customCount = BitConverter.ToInt32(h, p); p += 4;
        p += customCount * 20;
        int totalHeader = BitConverter.ToInt32(h, p); p += 4;
        Str(h, ref p); // folder name
        p += 4; // package flags
        int nameCount = BitConverter.ToInt32(h, p); p += 4;
        int nameOffset = BitConverter.ToInt32(h, p); p += 4;
        p += 8; // gatherable text data count, offset
        int exportCount = BitConverter.ToInt32(h, p); p += 4;
        int exportOffset = BitConverter.ToInt32(h, p); p += 4;
        int importCount = BitConverter.ToInt32(h, p); p += 4;
        int importOffset = BitConverter.ToInt32(h, p); p += 4;

        Names = new string[nameCount];
        int q = nameOffset;
        for (int i = 0; i < nameCount; i++) { Names[i] = Str(h, ref q); q += 4; }

        q = importOffset;
        for (int i = 0; i < importCount; i++)
        {
            q += 8 + 8 + 4; // class package, class name, outer index
            string objName = NameAt(h, q); q += 8;
            Imports.Add(objName);
        }

        _d = h.Concat(uexp).ToArray(); // serial offsets are relative to the start of the .uasset
        q = exportOffset;
        for (int i = 0; i < exportCount; i++)
        {
            int classIndex = BitConverter.ToInt32(h, q);
            string name = NameAt(h, q + 16);
            long serialSize = BitConverter.ToInt64(h, q + 28);
            long serialOffset = BitConverter.ToInt64(h, q + 36);
            q += 104;
            var e = new Export
            {
                Name = name,
                Class = classIndex < 0 && -classIndex - 1 < Imports.Count ? Imports[-classIndex - 1] : $"export{classIndex}",
            };
            _p = (int)serialOffset;
            try
            {
                ReadProps("", e.Props, (int)(serialOffset + serialSize));
                if (e.Class == "StringTable")
                {
                    // Object GUID flag (int32), then FStringTable: namespace, key -> source string pairs.
                    I32();
                    Str(_d, ref _p);
                    int n = I32();
                    for (int k = 0; k < n; k++) { string key = Str(_d, ref _p); StringTable[key] = Str(_d, ref _p); }
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidDataException) { }
            Exports.Add(e);
        }
        _ = totalHeader;
    }

    private string NameAt(byte[] d, int p)
    {
        int index = BitConverter.ToInt32(d, p), number = BitConverter.ToInt32(d, p + 4);
        string s = index >= 0 && index < Names.Length ? Names[index] : $"<name{index}>";
        return number > 0 ? $"{s}_{number - 1}" : s;
    }

    private void ReadProps(string prefix, List<KeyValuePair<string, string>> o, int limit)
    {
        while (_p + 8 <= limit)
        {
            string name = FName();
            if (name == "None") return;
            if (name.StartsWith("<name", StringComparison.Ordinal)) throw new InvalidDataException("bad name");
            string type = FName();
            int size = I32();
            int arrayIndex = I32();
            string structName = "", inner = "";
            bool boolVal = false;
            switch (type)
            {
                case "StructProperty": structName = FName(); _p += 16; break;
                case "BoolProperty": boolVal = _d[_p++] != 0; break;
                case "ByteProperty": case "EnumProperty": FName(); break;
                case "ArrayProperty": case "SetProperty": inner = FName(); break;
                case "MapProperty": FName(); FName(); break;
            }
            if (_d[_p++] != 0) _p += 16;
            int end = _p + size;
            string key = prefix + name + (arrayIndex != 0 ? $"[{arrayIndex}]" : "");
            try
            {
                switch (type)
                {
                    case "BoolProperty": o.Add(new(key, boolVal ? "True" : "False")); break;
                    case "StructProperty": ReadStruct(key, structName, size, o, end); break;
                    case "ArrayProperty": ReadArray(key, inner, size, o, end); break;
                    case "ByteProperty": o.Add(new(key, size == 1 ? _d[_p].ToString() : FName())); break;
                    default:
                        var v = ReadValue(type);
                        if (v != null) o.Add(new(key, v));
                        break;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidDataException) { }
            _p = end;
        }
    }

    private void ReadStruct(string key, string structName, int size, List<KeyValuePair<string, string>> o, int limit)
    {
        switch (structName)
        {
            case "Guid": o.Add(new(key, Convert.ToHexString(_d, _p, 16))); _p += 16; return;
            case "Vector": case "Rotator": _p += 12; return;
            case "Vector2D": case "IntPoint": _p += 8; return;
            case "LinearColor": case "Quat": _p += 16; return;
            case "Color": o.Add(new(key, Convert.ToHexString(_d, _p, 4))); _p += 4; return;
            case "SoftObjectPath": case "SoftClassPath":
                o.Add(new(key, Str(_d, ref _p))); Str(_d, ref _p); return;
        }
        ReadProps(key + ".", o, limit);
    }

    private void ReadArray(string key, string inner, int size, List<KeyValuePair<string, string>> o, int limit)
    {
        int count = I32();
        if (inner == "StructProperty")
        {
            FName(); FName(); I32(); I32();
            string structName = FName(); _p += 16; _p++;
            for (int i = 0; i < count; i++) ReadStruct($"{key}[{i}]", structName, -1, o, limit);
            return;
        }
        if (inner == "ByteProperty" && size - 4 == count) return;
        for (int i = 0; i < count; i++)
        {
            var v = ReadValue(inner);
            if (v == null) return;
            o.Add(new($"{key}[{i}]", v));
        }
    }

    private string? ReadValue(string type)
    {
        switch (type)
        {
            case "IntProperty": return I32().ToString();
            case "UInt32Property": return ((uint)I32()).ToString();
            case "Int64Property": { long v = BitConverter.ToInt64(_d, _p); _p += 8; return v.ToString(); }
            case "FloatProperty": { float v = BitConverter.ToSingle(_d, _p); _p += 4; return v.ToString(System.Globalization.CultureInfo.InvariantCulture); }
            case "NameProperty": case "EnumProperty": case "ByteProperty": return FName();
            case "StrProperty": return Str(_d, ref _p);
            case "SoftObjectProperty": { var s = Str(_d, ref _p); Str(_d, ref _p); return s; }
            case "ObjectProperty": return ObjectRef(I32());
            case "TextProperty": return Text();
            default: return null;
        }
    }

    private string ObjectRef(int index) =>
        index < 0 && -index - 1 < Imports.Count ? "import:" + Imports[-index - 1]
        : index > 0 ? "export:" + index : "null";

    private string Text()
    {
        I32(); // flags
        sbyte history = (sbyte)_d[_p++];
        if (history == -1) return I32() != 0 ? Str(_d, ref _p) : "";
        if (history == 0) { Str(_d, ref _p); Str(_d, ref _p); return Str(_d, ref _p); }
        // A string-table reference: resolved later against the table asset (see StringTables).
        if (history == 11) { string table = FName(); string key = Str(_d, ref _p); return $"{StringTableMark}{table}|{key}"; }
        throw new InvalidDataException($"text history {history}");
    }

    private int I32() { int v = BitConverter.ToInt32(_d, _p); _p += 4; return v; }

    private string FName()
    {
        string s = NameAt(_d, _p);
        _p += 8;
        return s;
    }

    private static string Str(byte[] d, ref int p)
    {
        int len = BitConverter.ToInt32(d, p); p += 4;
        if (len == 0) return "";
        if (len > 0) { var s = Encoding.UTF8.GetString(d, p, len - 1); p += len; return s; }
        var w = Encoding.Unicode.GetString(d, p, (-len - 1) * 2); p += -len * 2; return w;
    }
}
