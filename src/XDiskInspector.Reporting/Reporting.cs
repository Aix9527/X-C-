using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using XDiskInspector.Core;

namespace XDiskInspector.Reporting;

public sealed record ReportMetadata(string Author, string Douyin, string GitHub);
public sealed record ReportEnvelope(ReportMetadata Metadata, ScanReport Report);

public sealed class JsonReportWriter
{
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public async Task WriteAsync(ScanReport report, string destinationPath, CancellationToken cancellationToken = default)
    {
        var envelope = new ReportEnvelope(new ReportMetadata("Aix", "xch03209527", "Aix9527/Codex-Doctor"), report);
        await using var stream = File.Create(destinationPath);
        await JsonSerializer.SerializeAsync(stream, envelope, CreateOptions(), cancellationToken);
    }

    public async Task<ScanReport> ReadAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(sourcePath);
        var envelope = await JsonSerializer.DeserializeAsync<ReportEnvelope>(stream, CreateOptions(), cancellationToken)
            ?? throw new InvalidDataException("报告文件为空或格式无效。");
        foreach (var candidate in envelope.Report.CleanupCandidates) candidate.Selected = false;
        ScanReportRuntimeState.MarkPersisted(envelope.Report);
        return envelope.Report;
    }
}

public sealed class HtmlReportWriter
{
    public async Task WriteAsync(ScanReport report, string destinationPath, CancellationToken cancellationToken = default)
    {
        var html = BuildHtml(report);
        await File.WriteAllTextAsync(destinationPath, html, new UTF8Encoding(false), cancellationToken);
    }

    public string BuildHtml(ScanReport report)
    {
        static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
        static string Size(long bytes) => ByteFormatter.Format(bytes);

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append("<title>X C盘巡检官扫描报告</title><style>body{font-family:'Segoe UI','Microsoft YaHei',sans-serif;background:#101418;color:#e9f2f7;margin:0;padding:32px}main{max-width:1200px;margin:auto}.card{background:#171d22;border:1px solid #29343c;border-radius:16px;padding:20px;margin:16px 0}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:12px}.metric{background:#0f1519;border-radius:12px;padding:16px}.cyan{color:#3bd9ff}.orange{color:#ffad42}.red{color:#ff5b67}table{width:100%;border-collapse:collapse}th,td{text-align:left;padding:10px;border-bottom:1px solid #29343c;vertical-align:top}th{color:#9eb3bf}code{word-break:break-all;color:#bcecff}.note{color:#9eb3bf}footer{color:#71838d;margin-top:28px}</style></head><body><main>");
        sb.Append("<h1>X C盘巡检官 · 扫描报告</h1>");
        sb.Append($"<p class=\"note\">扫描时间：{E(report.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))} · 程序版本：{E(report.ApplicationVersion)} · 规则版本：{E(report.RuleVersion)} · 管理员：{(report.IsAdministrator ? "是" : "否")} · 完整：{(report.IsComplete ? "是" : "否")}</p>");
        if (!report.IsComplete) sb.Append("<div class=\"card red\"><strong>结果不完整：</strong>扫描被取消或未完成，禁止据此执行批量清理。</div>");

        sb.Append("<section class=\"card\"><h2>容量概览</h2><div class=\"grid\">");
        sb.Append($"<div class=\"metric\"><span class=\"note\">磁盘总量</span><h3>{Size(report.DiskTotalBytes)}</h3></div>");
        sb.Append($"<div class=\"metric\"><span class=\"note\">已用</span><h3>{Size(report.DiskUsedBytes)}</h3></div>");
        sb.Append($"<div class=\"metric\"><span class=\"note\">可用</span><h3 class=\"cyan\">{Size(report.DiskFreeBytes)}</h3></div>");
        sb.Append($"<div class=\"metric\"><span class=\"note\">可访问文件逻辑总量</span><h3>{Size(report.AccessibleLogicalBytes)}</h3></div></div>");
        sb.Append("<p class=\"note\">逻辑文件大小与磁盘实际已用空间可能因稀疏文件、压缩、簇大小、系统保留空间等原因不同，本报告不把逻辑大小伪装成精确可回收空间。</p></section>");

        sb.Append("<section class=\"card\"><h2>主要占用</h2><table><thead><tr><th>目录</th><th>容量</th></tr></thead><tbody>");
        foreach (var item in report.MainOccupancies) sb.Append($"<tr><td><code>{E(item.Path)}</code></td><td>{Size(item.SizeBytes)}</td></tr>");
        sb.Append("</tbody></table></section>");

        sb.Append("<section class=\"card\"><h2>重点目录</h2><table><thead><tr><th>路径</th><th>软件/系统</th><th>容量</th><th>风险</th><th>删除后果</th><th>建议</th></tr></thead><tbody>");
        foreach (var item in report.HighlightedItems)
            sb.Append($"<tr><td><code>{E(item.Path)}</code></td><td>{E(item.Software)}</td><td>{Size(item.SizeBytes)}</td><td>{E(item.RiskText)}</td><td>{E(item.Consequence)}</td><td>{E(item.RecommendationText)}</td></tr>");
        sb.Append("</tbody></table></section>");

        sb.Append("<section class=\"card\"><h2>大文件</h2><table><thead><tr><th>路径</th><th>大小</th><th>识别</th><th>建议</th></tr></thead><tbody>");
        foreach (var item in report.LargeFiles)
            sb.Append($"<tr><td><code>{E(item.Path)}</code></td><td>{Size(item.SizeBytes)}</td><td>{E(item.Software ?? "未知")}</td><td>{E(item.RecommendationText)}</td></tr>");
        sb.Append("</tbody></table></section>");

        sb.Append("<footer>作者：Aix · 抖音：xch03209527 · GitHub：Aix9527/Codex-Doctor<br>报告只包含文件系统元数据，不包含文件内容、浏览器凭据、API Key 或其他密钥。</footer>");
        sb.Append("</main></body></html>");
        return sb.ToString();
    }
}

public static class ByteFormatter
{
    public static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }
}
