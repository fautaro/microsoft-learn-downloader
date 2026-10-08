using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace MicrosoftLearnDownloader;

public class Program
{
    private const string DefaultCourseUrl = "https://learn.microsoft.com/en-us/training/courses/ai-103t00";

    public static async Task Main(string[] args)
    {
        Console.WriteLine("==================================================");
        Console.WriteLine(" Microsoft Learn PDF Downloader (Playwright)");
        Console.WriteLine("==================================================");

        var urls = new List<string>();
        string outputDir = "output";
        bool outputDirSpecified = false;

        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--output" || args[i] == "-o") && i + 1 < args.Length)
            {
                outputDir = args[++i];
                outputDirSpecified = true;
            }
            else if (!args[i].StartsWith("--"))
            {
                urls.Add(CleanUrl(args[i]));
            }
        }

        if (urls.Count == 0)
        {
            Console.WriteLine("Enter Microsoft Learn URLs (one per line).");
            Console.WriteLine("Leave blank and press Enter to finish and start processing:");
            while (true)
            {
                Console.Write("> ");
                string? input = Console.ReadLine()?.Trim();
                if (string.IsNullOrWhiteSpace(input)) break;
                if (input.StartsWith("http", StringComparison.OrdinalIgnoreCase)) urls.Add(CleanUrl(input));
                else Console.WriteLine("  Please enter a valid URL starting with http.");
            }

            if (urls.Count == 0)
            {
                Console.WriteLine("No URLs entered. Using default course.");
                urls.Add(DefaultCourseUrl);
            }

            if (!outputDirSpecified)
            {
                Console.WriteLine($"\nEnter output directory (press Enter to use default: '{outputDir}'):");
                Console.Write("> ");
                string? customOutputDir = Console.ReadLine()?.Trim();
                if (!string.IsNullOrWhiteSpace(customOutputDir))
                {
                    outputDir = customOutputDir;
                }
            }
        }
        else
        {
            Console.WriteLine($"\nUsing {urls.Count} URLs from arguments.");
        }

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        
        var processor = new CourseProcessor(browser, outputDir);
        foreach (var u in urls)
        {
            Console.WriteLine($"\n==================================================");
            Console.WriteLine($" Processing: {u}");
            Console.WriteLine($"==================================================");
            await processor.ProcessUrlAsync(u);
        }
    }

    public static string CleanUrl(string url)
    {
        var uri = new Uri(url);
        return $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}";
    }
}

// ============================================================================
// Playwright Scraper
// ============================================================================
public class PlaywrightScraper
{
    public static async Task<List<string>> ExtractLinksAsync(IPage page, string filterStr)
    {
        var links = await page.EvaluateAsync<string[]>(@"() => {
            return Array.from(document.querySelectorAll('a[href]')).map(a => a.href);
        }");

        var result = new List<string>();
        foreach (var link in links)
        {
            if (link.Contains(filterStr))
            {
                string clean = Program.CleanUrl(link);
                if (!result.Contains(clean))
                    result.Add(clean);
            }
        }
        return result;
    }

    public static async Task<List<string>> GetLearningPathsFromCourseAsync(IPage page)
    {
        return await ExtractLinksAsync(page, "/paths/");
    }

    public static async Task<List<string>> GetModulesFromLearningPathAsync(IPage page)
    {
        var links = await ExtractLinksAsync(page, "/modules/");
        return links.Where(l => !l.Contains("/units/")).ToList();
    }

    public static async Task<List<string>> GetUnitsFromModuleAsync(IPage page, string moduleUrl)
    {
        string normalizedModulePrefix = moduleUrl.TrimEnd('/') + "/";
        var links = await ExtractLinksAsync(page, normalizedModulePrefix);
        return links.Where(l => l.StartsWith(normalizedModulePrefix, StringComparison.OrdinalIgnoreCase) && l.Length > normalizedModulePrefix.Length)
                    .OrderBy(ExtractSortKey)
                    .ToList();
    }

    private static int ExtractSortKey(string url)
    {
        var segment = url.TrimEnd('/').Split('/').Last();
        var match = Regex.Match(segment, @"^(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : int.MaxValue;
    }
}

// ============================================================================
// File Utilities
// ============================================================================
public static class FileUtils
{
    public const int MaxSafePathLength = 240;
    public const int MaxComponentLength = 50;

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string SanitizeFileName(string name, int maxLen = MaxComponentLength)
    {
        if (string.IsNullOrWhiteSpace(name)) return "item";

        var invalid = Path.GetInvalidFileNameChars();
        string clean = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        
        // Normalize whitespace and collapse multiple consecutive underscores
        clean = Regex.Replace(clean, @"\s+", " ").Trim();
        clean = Regex.Replace(clean, @"_+", "_");

        if (clean.Length > maxLen)
        {
            clean = clean[..maxLen];
        }

        // Windows restriction: directory/file names must not end with dots, spaces, or underscores
        clean = clean.TrimEnd('.', ' ', '_');

        if (string.IsNullOrWhiteSpace(clean)) clean = "item";

        if (ReservedDeviceNames.Contains(clean))
        {
            clean = $"_{clean}";
        }

        return clean;
    }

    public static string CreateUniqueDirectory(string parentDir, string dirName, int maxComponentLength = MaxComponentLength)
    {
        string fullParentDir = Path.GetFullPath(parentDir);

        // Budget available characters to never exceed safe Windows path limit (240 chars)
        int availableLength = Math.Min(maxComponentLength, Math.Max(10, MaxSafePathLength - fullParentDir.Length - 1));
        
        string sanitizedName = SanitizeFileName(dirName, availableLength);
        string targetPath = Path.Combine(fullParentDir, sanitizedName);

        if (!Directory.Exists(targetPath))
        {
            Directory.CreateDirectory(targetPath);
            return targetPath;
        }

        // Directory exists: append unique ID while guaranteeing name length stays within budget
        const int suffixLength = 9; // "_xxxxxxxx"
        int baseBudget = Math.Max(5, availableLength - suffixLength);
        string baseName = SanitizeFileName(sanitizedName, baseBudget);

        string id = Guid.NewGuid().ToString("N")[..8];
        string uniquePath = Path.Combine(fullParentDir, $"{baseName}_{id}");

        while (Directory.Exists(uniquePath))
        {
            id = Guid.NewGuid().ToString("N")[..8];
            uniquePath = Path.Combine(fullParentDir, $"{baseName}_{id}");
        }

        Console.WriteLine($"\n[Notice] Directory already exists: '{targetPath}'");
        Console.WriteLine($"         Preserved existing directory and created alternative with ID: '{uniquePath}'\n");

        Directory.CreateDirectory(uniquePath);
        return uniquePath;
    }

    public static string GetSafePdfPath(string targetDir, int index, string moduleTitle)
    {
        string fullTargetDir = Path.GetFullPath(targetDir);
        string prefix = $"{index:D2}-";
        const string extension = ".pdf";

        int availableBudget = MaxSafePathLength - fullTargetDir.Length - 1 - prefix.Length - extension.Length;
        int maxTitleLength = Math.Clamp(availableBudget, 5, MaxComponentLength);

        string sanitizedTitle = SanitizeFileName(moduleTitle, maxTitleLength);
        string fileName = $"{prefix}{sanitizedTitle}{extension}";
        return Path.Combine(fullTargetDir, fileName);
    }
}

// ============================================================================
// Course Processor
// ============================================================================
public class CourseProcessor
{
    private readonly IBrowser _browser;
    private readonly string _baseOutputDir;
    private readonly string[] _ignorePrefixes = ["Knowledge check", "Module assessment", "Exercise - "];

    public CourseProcessor(IBrowser browser, string baseOutputDir)
    {
        _browser = browser;
        _baseOutputDir = baseOutputDir;
    }

    public async Task ProcessUrlAsync(string url)
    {
        if (url.Contains("/courses/")) await ProcessBatchAsync(url, "Course", true);
        else if (url.Contains("/paths/")) await ProcessBatchAsync(url, "Learning Path", false);
        else if (url.Contains("/modules/")) await ProcessSingleModuleFlowAsync(url);
        else Console.WriteLine("URL not recognized as course (/courses/), path (/paths/), or module (/modules/).");
    }

    private async Task ProcessBatchAsync(string url, string typeLabel, bool isCourse)
    {
        var page = await _browser.NewPageAsync();
        Console.WriteLine($"Loading {typeLabel}: {url}...");
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        string title = await page.TitleAsync();

        string rootDir = FileUtils.CreateUniqueDirectory(_baseOutputDir, FileUtils.SanitizeFileName(title));

        Console.WriteLine($"\nOutput directory:\n{rootDir}");
        Console.WriteLine($"\n{typeLabel}:\n{title}");

        var pathsOrModules = isCourse 
            ? await PlaywrightScraper.GetLearningPathsFromCourseAsync(page) 
            : await PlaywrightScraper.GetModulesFromLearningPathAsync(page);

        Console.WriteLine($"\n{ (isCourse ? "Learning Paths" : "Modules") } discovered: {pathsOrModules.Count}\n");

        if (pathsOrModules.Count == 0)
        {
            Console.WriteLine($"ERROR: Expected {(isCourse ? "paths" : "modules")} but discovered 0.");
            return;
        }

        int generated = 0, validated = 0, failed = 0;

        for (int i = 0; i < pathsOrModules.Count; i++)
        {
            if (isCourse)
            {
                string pathUrl = pathsOrModules[i];
                await page.GotoAsync(pathUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                string lpTitle = await page.TitleAsync();
                
                string lpDir = FileUtils.CreateUniqueDirectory(rootDir, $"{i + 1:D2}-{FileUtils.SanitizeFileName(lpTitle)}");

                Console.WriteLine($"\n[{i + 1}/{pathsOrModules.Count}] Learning Path: {lpTitle}");
                var modules = await PlaywrightScraper.GetModulesFromLearningPathAsync(page);
                
                for (int j = 0; j < modules.Count; j++)
                {
                    Console.WriteLine("--------------------------------------------------");
                    Console.WriteLine($"[{j + 1:D2}/{modules.Count:D2}]");
                    if (await ProcessModuleCoreAsync(modules[j], j + 1, lpDir)) { generated++; validated++; }
                    else failed++;
                }
            }
            else
            {
                Console.WriteLine("--------------------------------------------------");
                Console.WriteLine($"[{i + 1:D2}/{pathsOrModules.Count:D2}]");
                if (await ProcessModuleCoreAsync(pathsOrModules[i], i + 1, rootDir)) { generated++; validated++; }
                else failed++;
            }
        }

        await page.CloseAsync();
        PrintSummary(isCourse ? 0 : pathsOrModules.Count, generated, validated, failed, rootDir);
    }

    private async Task ProcessSingleModuleFlowAsync(string moduleUrl)
    {
        var page = await _browser.NewPageAsync();
        await page.GotoAsync(moduleUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        string title = await page.TitleAsync();
        await page.CloseAsync();

        string moduleDir = FileUtils.CreateUniqueDirectory(_baseOutputDir, FileUtils.SanitizeFileName(title));

        Console.WriteLine($"\nOutput directory:\n{moduleDir}");
        Console.WriteLine($"\nModule:\n{title}");

        bool success = await ProcessModuleCoreAsync(moduleUrl, 1, moduleDir);
        PrintSummary(1, success ? 1 : 0, success ? 1 : 0, success ? 0 : 1, moduleDir);
    }

    private void PrintSummary(int discovered, int generated, int validated, int failed, string outDir)
    {
        Console.WriteLine("\n==================================================");
        Console.WriteLine("RESULT");
        Console.WriteLine("==================================================");
        if (discovered > 0)
        {
            Console.WriteLine($"Modules discovered:  {discovered}");
            Console.WriteLine($"Modules processed:   {discovered}");
        }
        Console.WriteLine($"Modules failed:      {failed}");
        Console.WriteLine();
        Console.WriteLine($"PDFs generated:      {generated}");
        Console.WriteLine($"PDFs validated:      {validated}");
        Console.WriteLine($"PDFs failed:         {failed}");
        Console.WriteLine();
        Console.WriteLine("Output:");
        Console.WriteLine(outDir);
        Console.WriteLine("==================================================");

        if (generated != validated || failed > 0)
            Environment.ExitCode = 1;
    }

    private async Task<bool> ProcessModuleCoreAsync(string moduleUrl, int index, string targetDir)
    {
        string moduleId = moduleUrl.TrimEnd('/').Split('/').Last();
        Console.WriteLine($"Module:\n{moduleId}\n");
        Console.WriteLine($"ID:\n{moduleId}\n");

        var page = await _browser.NewPageAsync();
        await page.GotoAsync(moduleUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        
        string moduleTitle = await page.TitleAsync();
        var unitLinks = await PlaywrightScraper.GetUnitsFromModuleAsync(page, moduleUrl);
        Console.WriteLine($"Units discovered: {unitLinks.Count}\n");

        if (unitLinks.Count == 0)
        {
            Console.WriteLine("     (No units found)");
            await page.CloseAsync();
            return false;
        }

        string tempDir = FileUtils.CreateUniqueDirectory(targetDir, ".temp_pdf");
        var unitPdfPaths = new List<string>();

        try
        {
            for (int i = 0; i < unitLinks.Count; i++)
            {
                string link = unitLinks[i];
                Console.WriteLine($"Downloading Unit {i+1}/{unitLinks.Count}: {link}");
                
                var response = await page.GotoAsync(link, new PageGotoOptions { WaitUntil = WaitUntilState.Load });
                string pageTitle = await page.TitleAsync();
                
                if (_ignorePrefixes.Any(p => pageTitle.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine($"  -> Skipped (Ignored Prefix): {pageTitle}");
                    continue;
                }

                // If the page has a Video/Text toggle, switch to Text
                try 
                {
                    bool hasTextButton = await page.EvaluateAsync<bool>("() => document.querySelector(\"input[name='video-or-text'][value='text']\") !== null");
                    if (hasTextButton) 
                    {
                        Console.WriteLine("     -> Switching to text format...");
                        await page.EvaluateAsync("() => document.querySelector(\"input[name='video-or-text'][value='text']\").parentElement.click()");
                        await Task.Delay(1500); // Wait for content change
                    }
                }
                catch (Exception) { /* Ignore UI errors */ }

                // Inject CSS to hide clutter UI
                await page.AddStyleTagAsync(new PageAddStyleTagOptions
                {
                    Content = @"
                        header, footer, nav, aside, .sidebar, #left-nav, .right-rail, 
                        .page-metadata, .xp-tag, .feedback-button, .display-none-print,
                        .interactive, .video, .next-step, .loc-hero { display: none !important; }
                        body, #main-column, .content { margin: 0 !important; padding: 0 !important; width: 100% !important; max-width: none !important; }
                    "
                });

                string tempPdf = Path.Combine(tempDir, $"unit_{i}.pdf");
                
                string safePageTitle = pageTitle.Replace("'", "&apos;").Replace("\"", "&quot;");
                string headerHtml = $"<div style='font-size:10px; color:gray; text-align:center; width:100%; font-family:sans-serif;'>{safePageTitle}</div>";

                await page.PdfAsync(new PagePdfOptions
                {
                    Path = tempPdf,
                    Format = "A4",
                    Margin = new Microsoft.Playwright.Margin { Top = "25mm", Bottom = "20mm", Left = "20mm", Right = "20mm" },
                    PrintBackground = true,
                    DisplayHeaderFooter = true,
                    HeaderTemplate = headerHtml,
                    FooterTemplate = "<div></div>" // Hide default Playwright footer
                });

                unitPdfPaths.Add(tempPdf);
            }

            if (unitPdfPaths.Count == 0)
            {
                Console.WriteLine("\n  -> No content units were downloaded for this module (all units were skipped or failed). Skipping merge.\n");
                return false;
            }

            Console.WriteLine("\nMerging PDFs...");
            string finalPdfPath = FileUtils.GetSafePdfPath(targetDir, index, moduleTitle);
            
            bool merged = MergePdfs(unitPdfPaths, finalPdfPath);
            if (!merged) return false;

            var info = new FileInfo(finalPdfPath);
            Console.WriteLine($"Writing:\n{info.FullName}\n");
            
            if (info.Exists && info.Length > 1024)
            {
                Console.WriteLine("PDF:");
                Console.WriteLine($"{info.Length / 1024} KB");
                
                // Quick validation using PdfSharpCore
                int pages = 0;
                bool isPdfValid = false;
                
                try 
                {
                    using (var pdfDoc = PdfReader.Open(info.FullName, PdfDocumentOpenMode.ReadOnly))
                    {
                        pages = pdfDoc.PageCount;
                        isPdfValid = true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PDF validation error]: {ex.Message}");
                }

                Console.WriteLine($"Pages: {pages}\n");
                Console.WriteLine("Validation:");
                Console.WriteLine($"[{(info.Exists ? "OK" : "FAIL")}] Exists");
                Console.WriteLine($"[{(isPdfValid ? "OK" : "FAIL")}] PDF validation");
                Console.WriteLine($"[{(pages > 0 ? "OK" : "FAIL")}] Pages > 0\n");

                if (info.Exists && isPdfValid && pages > 0)
                {
                    Console.WriteLine("SUCCESS");
                    return true;
                }
            }
            
            Console.WriteLine("FAILED VALIDATION");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR generating PDF");
            Console.WriteLine($"Module:\n{moduleId}");
            Console.WriteLine($"Exception:\n{ex.Message}");
            Console.WriteLine($"Stack trace:\n{ex.StackTrace}");
            return false;
        }
        finally
        {
            await page.CloseAsync();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    private bool MergePdfs(List<string> pdfPaths, string outputPath)
    {
        try
        {
            using var outputDocument = new PdfDocument();
            foreach (var path in pdfPaths)
            {
                using var inputDocument = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                for (int i = 0; i < inputDocument.PageCount; i++)
                {
                    outputDocument.AddPage(inputDocument.Pages[i]);
                }
            }
            outputDocument.Save(outputPath);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error merging PDFs: {ex.Message}");
            return false;
        }
    }
}