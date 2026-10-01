using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Tomboy.Latex
{
    public class LatexManager
    {
        public const string DEFAULT_HEADER = "\\documentclass[12pt]{article}\n" +
                                              "\\usepackage[dvips]{graphicx}\n" +
                                              "\\usepackage{amsmath}\n" +
                                              "\\usepackage{amssymb}\n" +
                                              "\\pagestyle{empty}\n" +
                                              "\\begin{document}\n" +
                                              "\\begin{gather*}\n";

        public const string DEFAULT_FOOTER = "\n\\end{gather*}\n" +
                                              "\\end{document}\n";

        private static readonly string[] LatexBlacklist = {
            "\\def", "\\let", "\\futurelet", "\\newcommand", "\\renewcommand",
            "\\else", "\\fi", "\\write", "\\input", "\\include", "\\chardef",
            "\\catcode", "\\makeatletter", "\\noexpand", "\\toksdef", "\\every",
            "\\errhelp", "\\errorstopmode", "\\scrollmode", "\\nonstopmode",
            "\\batchmode", "\\read", "\\csname", "\\newhelp", "\\relax",
            "\\afterground", "\\afterassignment", "\\expandafter", "\\special",
            "\\loop", "\\repeat", "\\toks", "\\output", "\\line", "\\mathcode",
            "\\name", "\\immediate"
        };

        private static LatexManager? instance;
        public static LatexManager Instance => instance ??= new LatexManager();

        private readonly ConcurrentDictionary<string, Gdk.Pixbuf> memoryCache = new();
        private readonly string diskCacheDir;
        private static bool? isAvailable;

        public LatexManager()
        {
            diskCacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "tomboy", "latex"
            );

            try
            {
                if (!Directory.Exists(diskCacheDir))
                {
                    Directory.CreateDirectory(diskCacheDir);
                }
            }
            catch { }
        }

        public static bool IsLatexAvailable()
        {
            if (isAvailable.HasValue) return isAvailable.Value;

            bool hasLatex = File.Exists("/usr/bin/latex") || Which("latex") != null;
            bool hasDvipng = File.Exists("/usr/bin/dvipng") || Which("dvipng") != null;
            isAvailable = hasLatex && hasDvipng;
            return isAvailable.Value;
        }

        private static string? Which(string cmd)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("which", cmd)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (p != null)
                {
                    string output = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(1000);
                    if (p.ExitCode == 0 && !string.IsNullOrEmpty(output) && File.Exists(output))
                        return output;
                }
            }
            catch { }
            return null;
        }

        public void ClearCache()
        {
            memoryCache.Clear();
            try
            {
                if (Directory.Exists(diskCacheDir))
                {
                    foreach (var file in Directory.GetFiles(diskCacheDir, "*.png"))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }

        public Gdk.Pixbuf? GetImage(string code, string? header, string? footer, Action<string, Gdk.Pixbuf>? onReady = null)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;

            if (string.IsNullOrWhiteSpace(header)) header = DEFAULT_HEADER;
            if (string.IsNullOrWhiteSpace(footer)) footer = DEFAULT_FOOTER;

            string realCode = ExtractRealCode(code);
            if (string.IsNullOrWhiteSpace(realCode)) return null;

            // Security check
            foreach (var blacklisted in LatexBlacklist)
            {
                if (realCode.IndexOf(blacklisted, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Console.WriteLine($"[LatexManager] Disallowed command in formula: {blacklisted}");
                    return null;
                }
            }

            string cacheKey = $"{header}::{realCode}::{footer}";

            // 1. Check in-memory cache
            if (memoryCache.TryGetValue(cacheKey, out var cachedPixbuf))
            {
                return cachedPixbuf;
            }

            // 2. Check on-disk cache
            string hash = ComputeHash(cacheKey);
            string cachedFile = Path.Combine(diskCacheDir, $"{hash}.png");
            if (File.Exists(cachedFile))
            {
                try
                {
                    var pb = new Gdk.Pixbuf(cachedFile);
                    memoryCache[cacheKey] = pb;
                    return pb;
                }
                catch
                {
                    try { File.Delete(cachedFile); } catch { }
                }
            }

            // 3. Queue asynchronous generation
            if (!IsLatexAvailable())
            {
                return null;
            }

            Task.Run(() =>
            {
                bool success = RenderLatex(realCode, header, footer, cachedFile);
                if (success && File.Exists(cachedFile))
                {
                    GLib.Idle.Add(() =>
                    {
                        try
                        {
                            var pb = new Gdk.Pixbuf(cachedFile);
                            memoryCache[cacheKey] = pb;
                            onReady?.Invoke(code, pb);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[LatexManager] Failed to load generated Pixbuf: {ex.Message}");
                        }
                        return false;
                    });
                }
            });

            return null;
        }

        private static string ExtractRealCode(string code)
        {
            string trimmed = code.Trim();
            if (trimmed.StartsWith("\\[") && trimmed.EndsWith("\\]") && trimmed.Length >= 4)
            {
                return trimmed.Substring(2, trimmed.Length - 4);
            }
            if (trimmed.StartsWith("\\(") && trimmed.EndsWith("\\)") && trimmed.Length >= 4)
            {
                return trimmed.Substring(2, trimmed.Length - 4);
            }
            if (trimmed.StartsWith("$") && trimmed.EndsWith("$") && trimmed.Length >= 2)
            {
                return trimmed.Substring(1, trimmed.Length - 2);
            }
            return trimmed;
        }

        private static string ComputeHash(string input)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static bool RenderLatex(string realCode, string header, string footer, string outputPngPath)
        {
            string tmpBase = Path.Combine(Path.GetTempPath(), $"tbltx_{Guid.NewGuid():N}");
            string texFile = $"{tmpBase}.tex";
            string dviFile = $"{tmpBase}.dvi";

            try
            {
                string cleanCode = realCode.Trim();
                string cleanHeader = header.Trim();
                string cleanFooter = footer.Trim();

                string fullContent;
                if (cleanCode.Contains("\\begin{align") || cleanCode.Contains("\\begin{gather"))
                {
                    // Avoid nested gather
                    string altHeader = cleanHeader.Replace("\\begin{gather*}", "").Trim();
                    string altFooter = cleanFooter.Replace("\\end{gather*}", "").Trim();
                    fullContent = $"{altHeader}\n{cleanCode}\n{altFooter}\n";
                }
                else
                {
                    fullContent = $"{cleanHeader}\n{cleanCode}\n{cleanFooter}\n";
                }

                File.WriteAllText(texFile, fullContent, Encoding.UTF8);

                // Run latex
                using var pLatex = new Process();
                pLatex.StartInfo = new ProcessStartInfo("latex", $"--interaction=nonstopmode -output-directory=\"{Path.GetTempPath()}\" \"{texFile}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetTempPath()
                };
                pLatex.Start();
                pLatex.WaitForExit(5000);
                if (pLatex.ExitCode != 0 || !File.Exists(dviFile))
                {
                    return false;
                }

                string dir = Path.GetDirectoryName(outputPngPath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // Run dvipng (high resolution 150 DPI, transparent background, tight bounding box)
                using var pDvipng = new Process();
                pDvipng.StartInfo = new ProcessStartInfo("dvipng", $"-D 150 -bg Transparent -T tight -o \"{outputPngPath}\" \"{dviFile}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetTempPath()
                };
                pDvipng.Start();
                pDvipng.WaitForExit(5000);

                return pDvipng.ExitCode == 0 && File.Exists(outputPngPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LatexManager] Render error: {ex.Message}");
                return false;
            }
            finally
            {
                string[] exts = { ".tex", ".log", ".aux", ".dvi" };
                foreach (var ext in exts)
                {
                    try
                    {
                        string f = tmpBase + ext;
                        if (File.Exists(f)) File.Delete(f);
                    }
                    catch { }
                }
            }
        }
    }
}
