using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DesktopSheet.UiCheck;

/// <summary>장면마다 무엇을 하고 무엇을 확인했는지 적는다. 실패한 확인이 하나라도 있으면 그 장면은 실패다.</summary>
internal sealed class Report
{
    private readonly StringBuilder _md = new();
    private readonly List<string> _failed = new();
    private string _scene = "";
    private bool _sceneFailed;

    public int Failures => _failed.Count;

    public void Scene(string name, Action body)
    {
        _scene = name;
        _sceneFailed = false;
        _md.AppendLine().AppendLine($"## {name}").AppendLine();
        try { body(); }
        catch (Exception e)
        {
            _sceneFailed = true;
            _md.AppendLine($"- 실패: 장면이 중간에 멈춤 — {e.GetType().Name}: {e.Message}");
        }
        if (_sceneFailed) _failed.Add(name);
        Console.WriteLine($"{(_sceneFailed ? "실패" : "통과")}  {name}");
    }

    public void Expect(string what, string? expected, string? actual)
    {
        bool ok = expected == actual;
        if (!ok) _sceneFailed = true;
        _md.AppendLine($"- {(ok ? "통과" : "실패")}: {what} — 기대 `{Show(expected)}`, 실제 `{Show(actual)}`");
    }

    public void Note(string text) => _md.AppendLine($"- {text}");

    public void Shot(string file, string caption) => _md.AppendLine($"- 그림 `{file}` — {caption}");

    public void Write(string path)
    {
        var head = new StringBuilder();
        head.AppendLine("# 화면 시험 결과").AppendLine();
        head.AppendLine(_failed.Count == 0
            ? "모든 장면이 통과했습니다."
            : $"실패한 장면 {_failed.Count}개: {string.Join(", ", _failed)}");
        File.WriteAllText(path, head.ToString() + _md, Encoding.UTF8);
    }

    private static string Show(string? s) => s is null ? "(빈 칸)" : s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
}
