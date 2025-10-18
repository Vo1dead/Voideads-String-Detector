using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;

string outputStatusPath = Path.Combine(Environment.CurrentDirectory, "errors.txt");
string outputStringsPath = Path.Combine(Environment.CurrentDirectory, "javaw.txt");
string outputMatchesPath = Path.Combine(Environment.CurrentDirectory, "detections.txt");

// Use raw.githubusercontent.com without token/query
const string githubRawUrl = "https://raw.githubusercontent.com/Vo1dead/Voideads-String-Detector/refs/heads/master/strings.txt";

string? targetPath = args.Length > 0 ? args[0] : null;
if (string.IsNullOrWhiteSpace(targetPath))
{
    targetPath = FindJavawPathFromProcess();
    if (string.IsNullOrWhiteSpace(targetPath))
        targetPath = FindJavawPathFromEnvironment();
}

if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
{
    Console.Error.WriteLine("javaw.exe path not found. Provide path as first argument or start javaw and run again.");
    return;
}

File.WriteAllText(outputStatusPath, $"Using file: {targetPath}{Environment.NewLine}");

List<string> list;
try
{
    var data = File.ReadAllBytes(targetPath);
    var strings = new HashSet<string>(StringComparer.Ordinal);

    foreach (var s in ExtractAsciiStrings(data, 4)) strings.Add(s);
    foreach (var s in ExtractUtf16LeStrings(data, 4)) strings.Add(s);

    list = strings.OrderBy(s => s).ToList();
    File.WriteAllLines(outputStringsPath, list, Encoding.UTF8);

    Console.WriteLine($"Found {list.Count} strings. Written to: {outputStringsPath}");
}
catch (Exception ex)
{
    File.WriteAllText(outputStatusPath, "ERROR reading file: " + ex.Message + Environment.NewLine);
    Console.Error.WriteLine("Error reading file: " + ex.Message);
    return;
}

// Fetch the GitHub file (raw URL is embedded above) and compare
try
{
    using var http = new HttpClient();
    var resp = http.GetAsync(githubRawUrl).GetAwaiter().GetResult();

    if (!resp.IsSuccessStatusCode)
    {
        var err = $"GitHub fetch failed: {(int)resp.StatusCode} {resp.ReasonPhrase}{Environment.NewLine}";
        File.AppendAllText(outputStatusPath, err, Encoding.UTF8);
        // create a visible detections file so you can see the problem
        File.WriteAllText(outputMatchesPath, $"ERROR fetching remote strings: {(int)resp.StatusCode} {resp.ReasonPhrase}{Environment.NewLine}", Encoding.UTF8);
        Console.Error.WriteLine(err);
        return;
    }

    var remoteContent = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

    var matches = new List<string>();
    foreach (var s in list)
    {
        if (string.IsNullOrWhiteSpace(s)) continue;
        if (remoteContent.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
            matches.Add(s);
    }

    if (matches.Count == 0)
    {
        File.WriteAllText(outputMatchesPath, "No matches found." + Environment.NewLine, Encoding.UTF8);
        Console.WriteLine("No matches found.");
    }
    else
    {
        File.WriteAllLines(outputMatchesPath, matches.Distinct().OrderBy(m => m), Encoding.UTF8);
        Console.WriteLine($"Found {matches.Count} string(s). Written to: {outputMatchesPath}");
    }
}
catch (Exception ex)
{
    File.AppendAllText(outputStatusPath, $"ERROR fetching/checking GitHub file: {ex}{Environment.NewLine}", Encoding.UTF8);
    Console.Error.WriteLine("Error fetching strings file: " + ex.Message);
}

static string? FindJavawPathFromProcess()
{
    try
    {
        var proc = Process.GetProcessesByName("javaw").FirstOrDefault();
        if (proc != null)
        {
            try
            {
                return proc.MainModule?.FileName;
            }
            catch
            {
                // ignore permission/bitness issues when accessing MainModule
            }
        }
    }
    catch
    {
        // ignore failures enumerating processes
    }

    return null;
}

static string? FindJavawPathFromEnvironment()
{
    try
    {
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            var candidate = Path.Combine(javaHome, "bin", "javaw.exe");
            if (File.Exists(candidate)) return candidate;
        }
    }
    catch { }

    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "where",
            Arguments = "javaw.exe",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        if (p != null)
        {
            string? line = p.StandardOutput.ReadLine();
            p.WaitForExit(2000);
            if (!string.IsNullOrWhiteSpace(line) && File.Exists(line)) return line;
        }
    }
    catch { }

    return null;
}

static IEnumerable<string> ExtractAsciiStrings(byte[] data, int minLen)
{
    var sb = new StringBuilder();
    foreach (var b in data)
    {
        if (b >= 32 && b <= 126) sb.Append((char)b);
        else { if (sb.Length >= minLen) yield return sb.ToString(); sb.Clear(); }
    }
    if (sb.Length >= minLen) yield return sb.ToString();
}

static IEnumerable<string> ExtractUtf16LeStrings(byte[] data, int minLen)
{
    var sb = new StringBuilder();
    for (int i = 0; i + 1 < data.Length; i += 2)
    {
        char c = (char)(data[i] | (data[i + 1] << 8));
        if (!char.IsControl(c)) sb.Append(c);
        else { if (sb.Length >= minLen) yield return sb.ToString(); sb.Clear(); }
    }
    if (sb.Length >= minLen) yield return sb.ToString();
}


