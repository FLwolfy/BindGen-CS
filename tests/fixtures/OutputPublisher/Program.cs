using System;
using System.IO;
using System.Text.Json;
using BGCS.Hosting.Standard;
using BGCS.Core.Hosting;
using System.Threading.Tasks;

using BGCS.Infrastructure.IO;

string destination = Path.GetFullPath(args[0]);
string acquired = Path.GetFullPath(args[1]);
string release = Path.GetFullPath(args[2]);
using HostServices host = new HostServicesProvider().Create(new(Path.GetDirectoryName(destination)!, JsonSerializer.SerializeToElement(new { })));
using var transaction = new OutputDirectoryTransaction(destination, host.paths);
File.WriteAllText(Path.Combine(transaction.stagingPath, "value.txt"), "child");
File.WriteAllText(acquired, "owned");
while (!File.Exists(release))
    await Task.Delay(20);
transaction.Commit();
