using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopSheet.Core;

/// <summary>13.1 이 파일에 담는 창 상태. 14.6 이 이것으로 창을 되살린다.</summary>
public sealed class WindowState
{
    /// <summary>아직 자리를 잡은 적이 없으면 값이 없다. 그때는 화면 가운데에 띄운다(14.6).</summary>
    public double? X { get; set; }
    public double? Y { get; set; }
    public double Width { get; set; } = 560;
    public double Height { get; set; } = 444;
    public int Sheet { get; set; }
    public int Row { get; set; }
    public int Col { get; set; }
}

public enum LoadOutcome { Loaded, RestoredFromPrev, StartedEmpty }

public sealed record LoadResult(Workbook Book, WindowState Window, LoadOutcome Outcome, string? BrokenFile);

/// <summary>
/// 사양서 13장. 파일 하나에 다섯 시트를 담고, 임시 파일에 쓴 뒤 이름을 바꿔 넣는다.
/// 밀려난 파일이 book.prev.json 으로 남아 파일이 깨졌을 때 되살릴 것이 된다.
/// </summary>
public sealed class BookStore(string directory)
{
    public string Directory { get; } = directory;
    public string BookPath => Path.Combine(Directory, "book.json");
    public string PrevPath => Path.Combine(Directory, "book.prev.json");
    private string TempPath => Path.Combine(Directory, "book.tmp");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string DefaultDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopSheet");

    public LoadResult Load()
    {
        if (TryRead(BookPath, out LoadResult? ok)) return ok! with { Outcome = LoadOutcome.Loaded };
        if (TryRead(PrevPath, out LoadResult? prev)) return prev! with { Outcome = LoadOutcome.RestoredFromPrev };

        string? broken = null;
        if (File.Exists(BookPath))
        {
            broken = Path.Combine(Directory,
                $"book.broken-{DateTime.Now:yyyyMMdd-HHmm}.json");
            try { File.Move(BookPath, broken, overwrite: true); } catch (IOException) { broken = null; }
        }
        return new LoadResult(new Workbook(), new WindowState(), LoadOutcome.StartedEmpty, broken);
    }

    private bool TryRead(string path, out LoadResult? result)
    {
        result = null;
        if (!File.Exists(path)) return false;
        try
        {
            BookDto? dto = JsonSerializer.Deserialize<BookDto>(File.ReadAllText(path), Options);
            if (dto?.Sheets is null || dto.Sheets.Count == 0) return false;
            result = new LoadResult(ToWorkbook(dto), dto.Window ?? new WindowState(), LoadOutcome.Loaded, null);
            return true;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>13.3. 임시 파일에 전부 쓴 뒤 이름을 바꿔 넣는다. 반쯤 쓰이다 만 파일이 남지 않는다.</summary>
    public void Save(Workbook book, WindowState window)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(TempPath, JsonSerializer.Serialize(ToDto(book, window), Options));

        if (File.Exists(BookPath)) File.Replace(TempPath, BookPath, PrevPath, ignoreMetadataErrors: true);
        else File.Move(TempPath, BookPath);
    }

    internal static BookDto ToDto(Workbook book, WindowState window)
    {
        var dto = new BookDto { Version = 1, Window = window, Sheets = new List<SheetDto>() };
        foreach (Sheet sheet in book.Sheets)
        {
            var cells = new Dictionary<string, CellDto>();
            foreach ((int key, Cell cell) in sheet.Cells)
            {
                if (cell.IsEmpty) continue;
                cells[key.ToString(CultureInfo.InvariantCulture)] = new CellDto
                {
                    Raw = cell.Raw.Length > 0 ? cell.Raw : null,
                    Format = cell.FormatCode.Length > 0 ? cell.FormatCode : null,
                    Shade = cell.Shade,
                    Ink = cell.Ink,
                };
            }
            dto.Sheets.Add(new SheetDto { Name = sheet.Name, Cells = cells });
        }
        return dto;
    }

    internal static Workbook ToWorkbook(BookDto dto)
    {
        var book = new Workbook();
        for (int i = 0; i < dto.Sheets!.Count && i < Workbook.MaxSheets; i++)
        {
            SheetDto sd = dto.Sheets[i];
            Sheet sheet = i == 0 ? book.Sheets[0] : book.AddSheet(sd.Name);
            sheet.Name = sd.Name ?? sheet.Name;
        }

        for (int i = 0; i < dto.Sheets.Count && i < Workbook.MaxSheets; i++)
        {
            if (dto.Sheets[i].Cells is not { } cells) continue;
            foreach ((string key, CellDto cd) in cells)
            {
                if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out int flat)) continue;
                int row = flat / Sheet.Cols, col = flat % Sheet.Cols;
                if (!Sheet.InRange(row, col)) continue;

                var at = new CellAddress(i, row, col);
                if (cd.Raw is { Length: > 0 }) book.SetInput(at, cd.Raw);
                Cell cell = book.Sheets[i].GetOrCreate(row, col);
                if (cd.Format is not null) cell.FormatCode = cd.Format;
                cell.Shade = cd.Shade;
                cell.Ink = cd.Ink;
            }
        }
        return book;
    }

    internal sealed class BookDto
    {
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("window")]  public WindowState? Window { get; set; }
        [JsonPropertyName("sheets")]  public List<SheetDto>? Sheets { get; set; }
    }

    internal sealed class SheetDto
    {
        [JsonPropertyName("name")]  public string? Name { get; set; }
        [JsonPropertyName("cells")] public Dictionary<string, CellDto>? Cells { get; set; }
    }

    internal sealed class CellDto
    {
        [JsonPropertyName("r")] public string? Raw { get; set; }
        [JsonPropertyName("f")] public string? Format { get; set; }
        [JsonPropertyName("s")] public string? Shade { get; set; }
        [JsonPropertyName("i")] public string? Ink { get; set; }
    }
}
