// Repl.cs — h-opc-cli (custom)
// Version: 1.2.3 — 2025-08-16
// Changes since 1.2.2:
//  - NormalizeDec: if input has ',' and no '.', convert ',' -> '.' before parsing
//  - Parsers (float/double/decimal) call NormalizeDec first
// Notes kept from 1.2.2:
//  - Write: fallback for decimal input when server reports integer (try float then double)
//  - Type hint optional: write TAG[:type] value

using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Hylasoft.Opc.Common;

namespace Hylasoft.Opc.Cli
{
    internal class Repl
    {
        private readonly IClient<Node> _client;
        private Node _currentNode;
        private bool _keepAlive = true;

        public Repl(IClient<Node> client)
        {
            _client = client;
            _currentNode = client.RootNode;
        }

        public void Start()
        {
            while (_keepAlive)
            {
                try
                {
                    Console.WriteLine();
                    Console.Write(_currentNode.Tag + ": ");
                    var line = Console.ReadLine();
                    var command = CreateCommand(line);
                    RunCommand(command);
                }
                catch (BadCommandException)
                {
                    Console.WriteLine("Invalid command or arguments");
                    Console.WriteLine();
                    RunCommand(new Command(SupportedCommands.Help));
                }
                catch (Exception e)
                {
                    Console.WriteLine("An error occurred running the last command:");
                    Console.WriteLine(e.Message);
                }
            }
        }

        private void RunCommand(Command command)
        {
            switch (command.Cmd)
            {
                case SupportedCommands.Help: ShowHelp(); break;
                case SupportedCommands.Read: Read(command.Args); break;
                case SupportedCommands.Write: Write(command.Args); break;
                case SupportedCommands.Ls: ShowSubnodes(); break;
                case SupportedCommands.Root: _currentNode = _client.RootNode; break;
                case SupportedCommands.Up: _currentNode = _currentNode.Parent ?? _client.RootNode; break;
                case SupportedCommands.Monitor: Monitor(command.Args); break;
                case SupportedCommands.Cd: Cd(command.Args); break;
                case SupportedCommands.Exit: _client.Dispose(); _keepAlive = false; break;
                default: throw new BadCommandException();
            }
        }

        // ==== helpers ====
        private static string NormalizeDec(string s)
        {
            s = (s ?? string.Empty).Trim();
            // if user typed comma and there is no dot, convert comma -> dot
            if (s.IndexOf(',') >= 0 && s.IndexOf('.') < 0)
                s = s.Replace(',', '.');
            return s;
        }

        private static float ParseFloatFlexible(string s)
        {
            s = NormalizeDec(s);
            if (float.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var f1)) return f1;
            if (float.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out var f2)) return f2;
            var swapped = s.Contains(",") ? s.Replace(",", ".") : s.Replace(".", ",");
            if (float.TryParse(swapped, NumberStyles.Any, CultureInfo.InvariantCulture, out var f3)) return f3;
            if (float.TryParse(swapped, NumberStyles.Any, CultureInfo.CurrentCulture, out var f4)) return f4;
            throw new FormatException("Invalid numeric value for Single: " + s);
        }

        private static double ParseDoubleFlexible(string s)
        {
            s = NormalizeDec(s);
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d1)) return d1;
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out var d2)) return d2;
            var swapped = s.Contains(",") ? s.Replace(",", ".") : s.Replace(".", ",");
            if (double.TryParse(swapped, NumberStyles.Any, CultureInfo.InvariantCulture, out var d3)) return d3;
            if (double.TryParse(swapped, NumberStyles.Any, CultureInfo.CurrentCulture, out var d4)) return d4;
            throw new FormatException("Invalid numeric value for Double: " + s);
        }

        private static decimal ParseDecimalFlexible(string s)
        {
            s = NormalizeDec(s);
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var m1)) return m1;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out var m2)) return m2;
            var swapped = s.Contains(",") ? s.Replace(",", ".") : s.Replace(".", ",");
            if (decimal.TryParse(swapped, NumberStyles.Any, CultureInfo.InvariantCulture, out var m3)) return m3;
            if (decimal.TryParse(swapped, NumberStyles.Any, CultureInfo.CurrentCulture, out var m4)) return m4;
            throw new FormatException("Invalid numeric value for Decimal: " + s);
        }

        private static bool ParseBoolFlexible(string s)
        {
            if (bool.TryParse(s, out var b)) return b;
            if (s == "1" || s.Equals("on", StringComparison.OrdinalIgnoreCase) || s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (s == "0" || s.Equals("off", StringComparison.OrdinalIgnoreCase) || s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            throw new FormatException("Invalid boolean value: " + s);
        }

        private static bool LooksDecimal(string s) => s.IndexOf('.') >= 0 || s.IndexOf(',') >= 0;

        private static bool TrySplitTypeHint(string rawTag, out string baseTag, out string hint)
        {
            var idx = rawTag.LastIndexOf(':');
            if (idx > 0 && idx < rawTag.Length - 1)
            {
                baseTag = rawTag.Substring(0, idx);
                hint = rawTag.Substring(idx + 1);
                return true;
            }
            baseTag = rawTag; hint = null; return false;
        }

        private static Type MapTypeHint(string hint)
        {
            switch (hint.ToLowerInvariant())
            {
                case "single": case "float": case "f": return typeof(float);
                case "double": case "d": return typeof(double);
                case "decimal": case "dec": return typeof(decimal);
                case "int": case "i32": case "int32": return typeof(int);
                case "i16": case "int16": return typeof(short);
                case "i64": case "int64": return typeof(long);
                case "u16": case "uint16": return typeof(ushort);
                case "u32": case "uint32": return typeof(uint);
                case "u64": case "uint64": return typeof(ulong);
                case "bool": case "boolean": return typeof(bool);
                case "string": case "str": case "s": return typeof(string);
                case "byte": return typeof(byte);
                case "sbyte": return typeof(sbyte);
                default: return null;
            }
        }

        // ==== commands ====
        private void Cd(IList<string> args)
        {
            if (!args.Any()) throw new BadCommandException();
            _currentNode = _client.FindNode(GenerateRelativeTag(args[0]));
        }

        private void Write(IList<string> args)
        {
            if (args.Count < 2) throw new BadCommandException();

            var rawTag = args[0];
            var rawVal = args[1];

            // optional: TAG:type
            if (TrySplitTypeHint(rawTag, out var baseTag, out var hint) && !string.IsNullOrWhiteSpace(hint))
            {
                var fullTagHint = GenerateRelativeTag(baseTag);
                var t = MapTypeHint(hint);
                try
                {
                    if (t == typeof(float)) _client.Write<float>(fullTagHint, ParseFloatFlexible(rawVal));
                    else if (t == typeof(double)) _client.Write<double>(fullTagHint, ParseDoubleFlexible(rawVal));
                    else if (t == typeof(decimal)) _client.Write<decimal>(fullTagHint, ParseDecimalFlexible(rawVal));
                    else if (t == typeof(int)) _client.Write<int>(fullTagHint, Convert.ToInt32(rawVal));
                    else if (t == typeof(short)) _client.Write<short>(fullTagHint, Convert.ToInt16(rawVal));
                    else if (t == typeof(long)) _client.Write<long>(fullTagHint, Convert.ToInt64(rawVal));
                    else if (t == typeof(ushort)) _client.Write<ushort>(fullTagHint, Convert.ToUInt16(rawVal));
                    else if (t == typeof(uint)) _client.Write<uint>(fullTagHint, Convert.ToUInt32(rawVal));
                    else if (t == typeof(ulong)) _client.Write<ulong>(fullTagHint, Convert.ToUInt64(rawVal));
                    else if (t == typeof(bool)) _client.Write<bool>(fullTagHint, ParseBoolFlexible(rawVal));
                    else if (t == typeof(byte)) _client.Write<byte>(fullTagHint, Convert.ToByte(rawVal));
                    else if (t == typeof(sbyte)) _client.Write<sbyte>(fullTagHint, Convert.ToSByte(rawVal));
                    else if (t == typeof(string)) _client.Write<string>(fullTagHint, rawVal);
                    else _client.Write<object>(fullTagHint, rawVal);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Write error (hint): " + ex.Message);
                }
                return;
            }

            var fullTag = GenerateRelativeTag(rawTag);
            try
            {
                var type = _client.GetDataType(fullTag);

                switch (type.Name)
                {
                    // if server reports integer but value looks decimal, try float then double
                    case "Int32":
                        if (LooksDecimal(rawVal))
                        {
                            try { _client.Write<float>(fullTag, ParseFloatFlexible(rawVal)); }
                            catch { _client.Write<double>(fullTag, ParseDoubleFlexible(rawVal)); }
                        }
                        else _client.Write<int>(fullTag, Convert.ToInt32(rawVal));
                        break;

                    case "Int16":
                        if (LooksDecimal(rawVal))
                        {
                            try { _client.Write<float>(fullTag, ParseFloatFlexible(rawVal)); }
                            catch { _client.Write<double>(fullTag, ParseDoubleFlexible(rawVal)); }
                        }
                        else _client.Write<short>(fullTag, Convert.ToInt16(rawVal));
                        break;

                    case "UInt16":
                        if (LooksDecimal(rawVal))
                        {
                            try { _client.Write<float>(fullTag, ParseFloatFlexible(rawVal)); }
                            catch { _client.Write<double>(fullTag, ParseDoubleFlexible(rawVal)); }
                        }
                        else _client.Write<ushort>(fullTag, Convert.ToUInt16(rawVal));
                        break;

                    case "UInt32":
                        if (LooksDecimal(rawVal))
                        {
                            try { _client.Write<float>(fullTag, ParseFloatFlexible(rawVal)); }
                            catch { _client.Write<double>(fullTag, ParseDoubleFlexible(rawVal)); }
                        }
                        else _client.Write<uint>(fullTag, Convert.ToUInt32(rawVal));
                        break;

                    case "Int64":
                        if (LooksDecimal(rawVal))
                        {
                            try { _client.Write<float>(fullTag, ParseFloatFlexible(rawVal)); }
                            catch { _client.Write<double>(fullTag, ParseDoubleFlexible(rawVal)); }
                        }
                        else _client.Write<long>(fullTag, Convert.ToInt64(rawVal));
                        break;

                    case "UInt64":
                        if (LooksDecimal(rawVal))
                        {
                            try { _client.Write<float>(fullTag, ParseFloatFlexible(rawVal)); }
                            catch { _client.Write<double>(fullTag, ParseDoubleFlexible(rawVal)); }
                        }
                        else _client.Write<ulong>(fullTag, Convert.ToUInt64(rawVal));
                        break;

                    case "Boolean": _client.Write<bool>(fullTag, ParseBoolFlexible(rawVal)); break;
                    case "Single": _client.Write<float>(fullTag, ParseFloatFlexible(rawVal)); break;
                    case "Double": _client.Write<double>(fullTag, ParseDoubleFlexible(rawVal)); break;
                    case "Decimal": _client.Write<decimal>(fullTag, ParseDecimalFlexible(rawVal)); break;
                    case "Byte": _client.Write<byte>(fullTag, Convert.ToByte(rawVal)); break;
                    case "SByte": _client.Write<sbyte>(fullTag, Convert.ToSByte(rawVal)); break;
                    case "String": _client.Write<string>(fullTag, rawVal); break;

                    default:
                        var current = _client.Read<object>(fullTag);
                        var currentVal = (current is ReadEvent<object> re) ? re.Value : current;

                        if (currentVal is float) { _client.Write<float>(fullTag, ParseFloatFlexible(rawVal)); break; }
                        if (currentVal is double) { _client.Write<double>(fullTag, ParseDoubleFlexible(rawVal)); break; }
                        if (currentVal is decimal) { _client.Write<decimal>(fullTag, ParseDecimalFlexible(rawVal)); break; }
                        if (currentVal is int) { _client.Write<int>(fullTag, Convert.ToInt32(rawVal)); break; }
                        if (currentVal is short) { _client.Write<short>(fullTag, Convert.ToInt16(rawVal)); break; }
                        if (currentVal is long) { _client.Write<long>(fullTag, Convert.ToInt64(rawVal)); break; }
                        if (currentVal is ushort) { _client.Write<ushort>(fullTag, Convert.ToUInt16(rawVal)); break; }
                        if (currentVal is uint) { _client.Write<uint>(fullTag, Convert.ToUInt32(rawVal)); break; }
                        if (currentVal is ulong) { _client.Write<ulong>(fullTag, Convert.ToUInt64(rawVal)); break; }
                        if (currentVal is bool) { _client.Write<bool>(fullTag, ParseBoolFlexible(rawVal)); break; }
                        if (currentVal is byte) { _client.Write<byte>(fullTag, Convert.ToByte(rawVal)); break; }
                        if (currentVal is sbyte) { _client.Write<sbyte>(fullTag, Convert.ToSByte(rawVal)); break; }
                        if (currentVal is string) { _client.Write<string>(fullTag, rawVal); break; }

                        _client.Write<float>(fullTag, ParseFloatFlexible(rawVal));
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Write error: " + ex.Message);
            }
        }

        private void Monitor(IList<string> args)
        {
            if (!args.Any()) throw new BadCommandException();

            var stopped = false;
            _client.Monitor<object>(GenerateRelativeTag(args[0]), (o, stop) =>
            {
                if (stopped) { stop(); return; }

                if (o is ReadEvent<object> re)
                    Console.WriteLine($"Value changed: {re.Value} | Type: {re.Value?.GetType().Name}");
                else
                    Console.WriteLine($"Value changed: {o} | Type: {o?.GetType().Name}");
            });

            Console.WriteLine("Started monitoring. Press any key to interrupt.");
            Console.ReadKey(true);
            stopped = true;
        }

        private void Read(IList<string> args)
        {
            if (!args.Any()) throw new BadCommandException();

            var result = _client.Read<object>(GenerateRelativeTag(args[0]));
            if (result is ReadEvent<object> re)
                Console.WriteLine("Value: " + re.Value + " | Type: " + re.Value?.GetType().Name);
            else
                Console.WriteLine("Value: " + result + " | Type: " + result?.GetType().Name);
        }

        private static void ShowHelp()
        {
            Console.WriteLine("Supported commands:");
            Console.WriteLine("  ls: Display the subnodes");
            Console.WriteLine("  cd [tag]: Visit a children node");
            Console.WriteLine("  read [tag]: Read the node");
            Console.WriteLine("  write [tag] [value]: Write value on node");
            Console.WriteLine("  root: Go to root node");
            Console.WriteLine("  up: Go up one folder");
            Console.WriteLine("  monitor [node]: monitor the node");
            Console.WriteLine("subnodes are separated by '.' The tag is relative to the current folder");
        }

        private void ShowSubnodes()
        {
            var nodes = _client.ExploreFolder(_currentNode.Tag);
            if (nodes == null || !nodes.Any()) Console.WriteLine("no subnodes");
            else foreach (var node in nodes) Console.WriteLine(node.Name);
        }

        private static Command CreateCommand(string line)
        {
            try
            {
                var cmd = CliUtils.SplitArguments(line);
                var args = cmd.Skip(1).ToList();
                if (!Enum.TryParse(cmd[0], true, out SupportedCommands selectedCommand))
                    selectedCommand = SupportedCommands.Help;
                return new Command(selectedCommand, args);
            }
            catch (Exception e)
            {
                throw new BadCommandException(e.Message, e);
            }
        }

        private string GenerateRelativeTag(string relativeTag)
        {
            var node = _client.ExploreFolder(_currentNode.Tag)
                .SingleOrDefault(n => n.Name == relativeTag);
            return node == null ? relativeTag : node.Tag;
        }
    }
}
