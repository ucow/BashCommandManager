using System.Text;

namespace BashCommandManager.Cli.Output;

/// <summary>等宽表格渲染；CJK 字符按 2 列计宽。</summary>
public static class TableWriter
{
    public static string Render(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var widths = headers.Select(DisplayWidth).ToArray();
        foreach (var row in rows)
        {
            for (var column = 0; column < widths.Length && column < row.Count; column++)
            {
                widths[column] = Math.Max(widths[column], DisplayWidth(row[column]));
            }
        }

        var builder = new StringBuilder();
        AppendRow(builder, headers, widths);
        foreach (var row in rows)
        {
            AppendRow(builder, row, widths);
        }

        return builder.ToString().TrimEnd('\n');
    }

    public static int DisplayWidth(string text)
    {
        var width = 0;
        foreach (var character in text)
        {
            width += character >= 0x2E80 ? 2 : 1;
        }
        return width;
    }

    private static void AppendRow(StringBuilder builder, IReadOnlyList<string> cells, int[] widths)
    {
        for (var column = 0; column < cells.Count; column++)
        {
            builder.Append(cells[column]);
            if (column < cells.Count - 1)
            {
                // 偏差记录（T4）：计划原文为 Math.Max(1, width - cellWidth) + 2，
                // 当单元格正好填满列宽时会渲染 3 个空格；测试契约要求固定 2 空格间隔。
                var padding = column < widths.Length ? widths[column] - DisplayWidth(cells[column]) : 0;
                builder.Append(' ', Math.Max(0, padding) + 2);
            }
        }
        builder.Append('\n');
    }
}
