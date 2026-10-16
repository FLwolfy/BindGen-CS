using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

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
