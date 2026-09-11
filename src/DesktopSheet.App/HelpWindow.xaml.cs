using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopSheet.App;

/// <summary>우클릭 메뉴의 도움말. 사양서 10장과 15장이 정한 키를 한자리에 모아 보여 준다.</summary>
public partial class HelpWindow : Window
{
    private static readonly (string Group, string Key, string What)[] Shortcuts =
    {
        ("옮기기", "방향키", "한 칸씩 옮김"),
        ("옮기기", "Shift+방향키", "고른 영역을 넓힘"),
        ("옮기기", "Ctrl+방향키", "값이 끊기는 데까지 건너뜀"),
        ("옮기기", "Enter / Shift+Enter", "아래로 / 위로"),
        ("옮기기", "Tab / Shift+Tab", "오른쪽으로 / 왼쪽으로"),
        ("옮기기", "Ctrl+A", "시트 전체를 고름"),
        ("옮기기", "머리글 클릭", "그 행이나 열 전체를 고름"),

        ("고치기", "F2 또는 더블클릭", "칸을 편집함"),
        ("고치기", "글자를 그냥 침", "바로 편집으로 들어감"),
        ("고치기", "Delete", "값과 수식만 지움. 서식은 남음"),
        ("고치기", "Ctrl+Z / Ctrl+Y", "되돌리기 / 다시 실행 (20단계)"),

        ("옮겨 담기", "Ctrl+C / Ctrl+X", "복사 / 잘라내기"),
        ("옮겨 담기", "Ctrl+V", "값과 수식을 붙임"),
        ("옮겨 담기", "Ctrl+Shift+V", "수식을 값으로 굳혀 붙임"),
        ("옮겨 담기", "Esc", "복사 표시를 지움"),

        ("꾸미기", "Ctrl+Shift+1~0", "셀 음영 열 가지"),
        ("꾸미기", "Alt+Shift+1~0", "글자색 열 가지"),
        ("꾸미기", "Ctrl+Shift+Space", "음영·글자색·숫자 서식을 지움"),

        ("창", "휠 / Shift+휠", "세로 / 가로로 굴림"),
        ("창", "탭 우클릭", "시트 이름 바꾸기, 지우기"),
        ("창", "트레이 아이콘 클릭", "창을 앞으로 부름"),
        ("창", "가장자리 끌기", "창 크기 조절"),
    };

    public HelpWindow()
    {
        InitializeComponent();
        Build();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    private void Build()
    {
        string? group = null;
        foreach ((string g, string key, string what) in Shortcuts)
        {
            if (g != group)
            {
                group = g;
                Rows.Children.Add(new TextBlock
                {
                    Text = g,
                    Margin = new Thickness(12, 12, 12, 4),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = PaletteBrushes.Of("#586070"),
                });
            }

            var row = new Grid { Margin = new Thickness(12, 1, 12, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var keyText = new TextBlock
            {
                Text = key, FontSize = 11.5, Foreground = PaletteBrushes.Of("#15181D"),
                FontFamily = new FontFamily("Consolas, Nanum Gothic Coding"),
                TextWrapping = TextWrapping.Wrap,
            };
            var whatText = new TextBlock
            {
                Text = what, FontSize = 11.5, Foreground = PaletteBrushes.Of("#586070"),
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(whatText, 1);
            row.Children.Add(keyText);
            row.Children.Add(whatText);
            Rows.Children.Add(row);
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button) return;
        DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
