using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace NJTruckCharging.Infrastructure.Excel;

/// <summary>
/// Minimal streaming (SAX) reader for .xlsx sheets: the candidate workbook has ~2 million cells,
/// so it is read row by row instead of loading a DOM.
/// </summary>
internal sealed class XlsxSheetReader : IDisposable
{
    private readonly SpreadsheetDocument _doc;
    private readonly string[] _sharedStrings;
    private readonly Dictionary<string, WorksheetPart> _sheets = new(StringComparer.OrdinalIgnoreCase);

    public XlsxSheetReader(Stream stream)
    {
        _doc = SpreadsheetDocument.Open(stream, false);
        var wbPart = _doc.WorkbookPart ?? throw new InvalidDataException("Workbook part missing.");

        var sst = wbPart.SharedStringTablePart?.SharedStringTable;
        _sharedStrings = sst?.Elements<SharedStringItem>().Select(si => si.InnerText).ToArray() ?? [];

        foreach (var sheet in wbPart.Workbook.Sheets?.Elements<Sheet>() ?? [])
        {
            if (sheet.Name?.Value is { } name && sheet.Id?.Value is { } id)
                _sheets[name] = (WorksheetPart)wbPart.GetPartById(id);
        }
    }

    public IReadOnlyCollection<string> SheetNames => _sheets.Keys;

    /// <summary>Rows of a sheet keyed by the header row (row 1). Values are string, double, bool or null.</summary>
    public IEnumerable<XlsxRow> ReadRows(string sheetName)
    {
        if (!_sheets.TryGetValue(sheetName, out var part))
            throw new InvalidDataException($"Sheet '{sheetName}' not found. Sheets: {string.Join(", ", _sheets.Keys)}");

        using var reader = OpenXmlReader.Create(part);
        Dictionary<string, int>? header = null;
        while (reader.Read())
        {
            if (reader.ElementType != typeof(Row) || !reader.IsStartElement) continue;
            var row = (Row)reader.LoadCurrentElement()!;
            var values = new Dictionary<int, object?>();
            foreach (var cell in row.Elements<Cell>())
            {
                var col = ColumnIndex(cell.CellReference?.Value);
                if (col >= 0) values[col] = CellValue(cell);
            }

            if (header is null)
            {
                header = values.Where(v => v.Value is not null)
                    .ToDictionary(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture)!.Trim(), v => v.Key,
                        StringComparer.OrdinalIgnoreCase);
                continue;
            }
            if (values.Count == 0 || values.Values.All(v => v is null)) continue;
            yield return new XlsxRow(header, values);
        }
    }

    private object? CellValue(Cell cell)
    {
        var type = cell.DataType?.Value;
        if (type == CellValues.InlineString) return cell.InlineString?.InnerText;

        var raw = cell.CellValue?.Text;
        if (raw is null) return null;

        if (type == CellValues.SharedString)
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i < _sharedStrings.Length
                ? _sharedStrings[i] : null;
        if (type == CellValues.Boolean) return raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase);
        if (type == CellValues.String || type == CellValues.Error) return raw;

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : raw;
    }

    private static int ColumnIndex(string? cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return -1;
        var index = 0;
        foreach (var ch in cellRef)
        {
            if (!char.IsLetter(ch)) break;
            index = index * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return index - 1;
    }

    public void Dispose() => _doc.Dispose();
}

/// <summary>A data row with typed, tolerant accessors by header name.</summary>
internal sealed class XlsxRow(Dictionary<string, int> header, Dictionary<int, object?> values)
{
    public bool Has(string column) => header.ContainsKey(column);

    public object? Raw(string column) =>
        header.TryGetValue(column, out var idx) && values.TryGetValue(idx, out var v) ? v : null;

    public string? Str(string column) => Raw(column) switch
    {
        null => null,
        string s when string.IsNullOrWhiteSpace(s) => null,
        string s => s.Trim(),
        double d => d.ToString("0.##########", CultureInfo.InvariantCulture),
        bool b => b ? "True" : "False",
        var o => Convert.ToString(o, CultureInfo.InvariantCulture)
    };

    /// <summary>Text that may have been stored as a number with leading zeros lost (ZIP, yymmdd dates).</summary>
    public string? Code(string column, int width) => Raw(column) switch
    {
        double d when d >= 0 && d == Math.Floor(d) => ((long)d).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0'),
        _ => Str(column)
    };

    /// <summary>Integer text (phone numbers, USDOT) without a decimal part.</summary>
    public string? Whole(string column) => Raw(column) switch
    {
        double d when d == Math.Floor(d) => ((long)d).ToString(CultureInfo.InvariantCulture),
        _ => Str(column)
    };

    public double? NDbl(string column) => Raw(column) switch
    {
        double d when !double.IsNaN(d) => d,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) => p,
        bool b => b ? 1 : 0,
        _ => null
    };

    public double Dbl(string column) => NDbl(column) ?? 0;
    public int? NInt(string column) => NDbl(column) is { } d ? (int)Math.Round(d) : null;
    public int Int(string column) => NInt(column) ?? 0;

    public bool Bool(string column) => Raw(column) switch
    {
        bool b => b,
        double d => d != 0,
        string s => s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1" || s.Equals("yes", StringComparison.OrdinalIgnoreCase),
        _ => false
    };
}
