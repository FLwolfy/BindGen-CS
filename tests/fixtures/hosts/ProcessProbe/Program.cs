using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

if (args[0] == "raw-utf8")
{
    using Stream output = Console.OpenStandardOutput();
    using Stream error = Console.OpenStandardError();
    output.Write(Encoding.UTF8.GetBytes(args[1]));
    error.Write(Encoding.UTF8.GetBytes(args[2]));
    Environment.ExitCode = 7;
    return;
}
if (args[0] == "child")
{
    Console.WriteLine("Inherited pipe is open.");
    await Task.Delay(TimeSpan.FromSeconds(30));
    return;
}
ProcessStartInfo start = new(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet")
    start.ArgumentList.Add(typeof(Program).Assembly.Location);
start.ArgumentList.Add("child");
using Process child = Process.Start(start)!;
File.WriteAllText(args[1], child.Id.ToString());
Console.WriteLine("Parent exited.");
