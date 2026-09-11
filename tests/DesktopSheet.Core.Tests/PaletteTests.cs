using DesktopSheet.Core;
using Xunit;

namespace DesktopSheet.Core.Tests;

/// <summary>사양서 15.2 와 15.3. 색을 고른 방식이 대비를 보장한다는 것을 여기서 확인한다.</summary>
public class PaletteTests
{
    [Fact]
    public void 열_가지다()
    {
        Assert.Equal(10, Palette.Entries.Length);
    }

    [Fact]
    public void 어느_음영에_자동_글자를_얹어도_4_5대1을_넘는다()
    {
        foreach (var e in Palette.Entries)
        {
            double r = Palette.Contrast(e.Shade, Palette.AutoInk(e.Shade));
            Assert.True(r >= 4.5, $"{e.Name} 음영 {e.Shade} 이 {r:F2}:1");
        }
    }

    [Fact]
    public void 가장_낮은_자동_짝이_빨강_음영의_5_25대1이다()
    {
        double worst = 99;
        string name = "";
        foreach (var e in Palette.Entries)
        {
            double r = Palette.Contrast(e.Shade, Palette.AutoInk(e.Shade));
            if (r < worst) { worst = r; name = e.Name; }
        }
        Assert.Equal("빨강", name);
        Assert.Equal(5.25, worst, 2);
    }

    [Fact]
    public void 검정과_파랑_음영에서만_흰_글자가_된다()
    {
        foreach (var e in Palette.Entries)
        {
            string expected = e.Name is "검" or "파랑" ? "#FFFFFF" : "#000000";
            Assert.Equal(expected, Palette.AutoInk(e.Shade));
        }
    }

    [Fact]
    public void 글자색은_흰_바탕에서_모두_읽힌다()
    {
        foreach (var e in Palette.Entries)
        {
            if (e.Name == "흰") continue;   // 흰 글자는 어두운 음영 위에 쓰는 것이다
            double r = Palette.Contrast(Palette.DefaultShade, e.Ink);
            Assert.True(r >= 4.5, $"{e.Name} 글자색 {e.Ink} 이 흰 바탕에서 {r:F2}:1");
        }
    }

    [Fact]
    public void 순색_그대로_글자에_쓰면_읽히지_않는다는_것이_이_설계의_이유다()
    {
        // 15.2 가 밝기를 내린 까닭. 순색 노랑·청록·초록은 흰 바탕에서 사라진다.
        Assert.True(Palette.Contrast("#FFFFFF", "#FFFF00") < 1.1);
        Assert.True(Palette.Contrast("#FFFFFF", "#00FFFF") < 1.3);
        Assert.True(Palette.Contrast("#FFFFFF", "#00FF00") < 1.4);
    }
}

public class CellErrorTests
{
    [Fact]
    public void 오류_값은_모두_표시_예산에_들어간다()
    {
        foreach (CellError e in System.Enum.GetValues<CellError>())
        {
            string s = CellErrorText.Of(e);
            Assert.True(s.Length <= DisplayFormatter.Budget, $"{s} 가 {s.Length}자");
        }
    }

    [Fact]
    public void 가장_긴_오류는_일곱_자다()
    {
        Assert.Equal(7, CellErrorText.Of(CellError.DivideByZero).Length);
    }
}
