using System.Diagnostics;
using System.Text;

// Child mode: print how THIS process received its argv, one entry per line,
// framed so the parent can parse it unambiguously.
if (args.Length >= 1 && args[0] == "--print-argv")
{
    var sb = new StringBuilder();
    for (int i = 1; i < args.Length; i++)
        sb.Append('[').Append(args[i]).Append("]\n");
    Console.Out.Write(sb.ToString());
    return 0;
}

// Exact copy of SourceGit's src/App.Extensions.cs Quoted()/Escaped().
static string Escaped(string value) => value.Replace("\"", "\\\"", StringComparison.Ordinal);
static string Quoted(string value) => $"\"{Escaped(value)}\"";

var self = Environment.ProcessPath!;

// Benign filenames. The invariant under test:
//   Quoted(name) must parse back to EXACTLY ONE argv entry equal to `name`.
// No git, no options, no payload -- just a correctness round-trip.
string[] names =
{
    "normal.txt",
    "with space.txt",
    "quote\".txt",
    @"back\slash.txt",
    @"trailing\",          // backslash immediately before the closing quote
    @"a\""b.txt",          // backslash then quote
};

int failures = 0;
foreach (var name in names)
{
    var psi = new ProcessStartInfo
    {
        FileName = self,
        Arguments = $"--print-argv {Quoted(name)}",   // mirrors `show {rev}:{file.Quoted()}`
        UseShellExecute = false,
        RedirectStandardOutput = true,
    };
    using var p = Process.Start(psi)!;
    var outp = p.StandardOutput.ReadToEnd();
    p.WaitForExit();

    var received = new List<string>();
    foreach (var line in outp.Split('\n'))
        if (line.StartsWith('[') && line.EndsWith(']'))
            received.Add(line[1..^1]);

    bool ok = received.Count == 1 && received[0] == name;
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  input={Display(name)}");
    Console.WriteLine($"      Arguments string = {Display(psi.Arguments)}");
    Console.WriteLine($"      parsed into {received.Count} argv entr{(received.Count==1?"y":"ies")}: {string.Join(" | ", received.Select(Display))}");
}

Console.WriteLine();
Console.WriteLine(failures == 0
    ? "All inputs round-tripped to the same single argument (Quoted is safe)."
    : $"{failures} input(s) did NOT round-trip: Quoted() produced a different argument than the input.");
return failures == 0 ? 0 : 1;

static string Display(string s) => "«" + s.Replace("\n","\\n") + "»";
