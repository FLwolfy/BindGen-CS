using System;
using CustomFunctionTable;
using Generated.CustomFunctionTable;

if (args.Length != 1) throw new ArgumentException("Pass the native custom-table library path.");
using var api = CustomTableApi.FromProvider(CustomSlotTable.Open(args[0]));
Console.WriteLine(api.Raw.Add(20, 22));
Console.WriteLine(api.Raw.Multiply(6, 7));
