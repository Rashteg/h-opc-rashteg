// Program.cs — h-opc-cli (custom)
// Version: 1.4.1 — 2025-08-19
// Interactive startup + OPC-DA discovery via COM (OPC.Automation - Late Binding).
// Compatible with C# 7.3 / .NET Framework 4.x (sem funcoes locais, sem PtrToStructure<T>).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Hylasoft.Opc.Common;
using Hylasoft.Opc.Da;
using Hylasoft.Opc.Ua;

namespace Hylasoft.Opc.Cli
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
                var version = fvi.FileVersion;
                Console.WriteLine("h-opc-cli v" + version);
                Initialize(args);
            }
            catch (Exception e)
            {
                Console.WriteLine("The application ended unexpectedly.");
                Console.WriteLine(e);
                Console.WriteLine("To file an issue, visit http://github.com/hylasoft-usa/h-opc/issues");
            }
        }

        private static void Initialize(string[] args)
        {
            // legado: 2 args
            if (args.Length == 2)
            {
                SupportedTypes type;
                try { type = GetOpcType(args[0]); }
                catch (ArgumentException)
                {
                    Console.WriteLine(args[0] + " is not a supported type");
                    Console.WriteLine("Supported types: " + GetSupportedTypes());
                    return;
                }

                try
                {
                    var client = GetClient(args[1], type);
                    client.Connect();
                    new Repl(client).Start();
                }
                catch
                {
                    Console.WriteLine("An error occured when trying connecting to the server");
                    throw;
                }
                return;
            }

            // interativo
            Console.WriteLine("Usage: h-opc-cli [Type] [serverurl]");
            Console.WriteLine("Supported types: " + GetSupportedTypes());
            Console.WriteLine();
            Console.WriteLine("No args provided. Entering interactive mode.");

            var selectedType = AskSupportedType();

            string finalUrl;
            if (selectedType == SupportedTypes.Da)
            {
                Console.Write("Host for DA discovery (blank = localhost): ");
                var host = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(host)) host = "localhost";

                var servers = EnumerateDaServersCom(host);
                if (servers.Count == 0)
                {
                    Console.WriteLine("No OPC DA servers found on host '" + host + "'.");
                    finalUrl = AskManualUrl(selectedType);
                }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine("OPC DA servers on " + host + ":");
                    for (int i = 0; i < servers.Count; i++)
                    {
                        var s = servers[i];
                        // Modificado para não exibir mais o Vendor, que não obtemos
                        Console.WriteLine("  [" + (i + 1) + "] " + s.ProgId);
                    }
                    Console.WriteLine("  [M] Manual URL");
                    Console.Write("Select: ");
                    var pick = Console.ReadLine();

                    if (string.Equals(pick, "m", StringComparison.OrdinalIgnoreCase))
                    {
                        finalUrl = AskManualUrl(selectedType);
                    }
                    else if (int.TryParse(pick, out var idx) && idx >= 1 && idx <= servers.Count)
                    {
                        finalUrl = "opcda://" + host + "/" + servers[idx - 1].ProgId;
                        Console.WriteLine("Chosen: " + finalUrl);
                    }
                    else
                    {
                        Console.WriteLine("Invalid selection.");
                        return;
                    }
                }
            }
            else
            {
                Console.Write("UA URL (ex: opc.tcp://host:port/endpoint): ");
                finalUrl = ReadNonEmpty();
            }

            try
            {
                var client = GetClient(finalUrl, selectedType);
                client.Connect();
                new Repl(client).Start();
            }
            catch
            {
                Console.WriteLine("An error occured when trying connecting to the server");
                throw;
            }
        }

        // --- DA discovery via COM (OPC.Automation - Late Binding) ---

        // Estrutura simplificada para remover os warnings de compilação
        private struct DaServerInfo
        {
            public string ProgId;
        }

        // MÉTODO FINAL USANDO LATE BINDING (INVOKEMEMBER)
        private static IList<DaServerInfo> EnumerateDaServersCom(string host)
        {
            var list = new List<DaServerInfo>();
            if (!string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) && !string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("DA discovery via OPC Automation only supports localhost.");
                return list;
            }

            object opcServerObject = null;
            try
            {
                var type = Type.GetTypeFromProgID("OPC.Automation.1");
                if (type == null)
                {
                    Console.WriteLine("DA discovery error: OPC Automation wrapper not found.");
                    return list;
                }

                opcServerObject = Activator.CreateInstance(type);

                // Usando Late Binding para chamar o método pelo nome, igual ao PowerShell
                object result = type.InvokeMember(
                    "GetOPCServers",
                    BindingFlags.InvokeMethod,
                    null,
                    opcServerObject,
                    new object[] { string.Empty } // Argumentos do método
                );

                var serverNames = result as Array;

                if (serverNames != null)
                {
                    foreach (string name in serverNames)
                    {
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            list.Add(new DaServerInfo { ProgId = name });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Acessa a exceção interna para ver a causa real do erro COM
                var baseException = ex.GetBaseException();
                Console.WriteLine("DA discovery error: " + baseException.Message);
            }
            finally
            {
                if (opcServerObject != null)
                {
                    Marshal.ReleaseComObject(opcServerObject);
                }
            }

            return list.OrderBy(s => s.ProgId, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // --- Métodos Utilitários ---

        private static string ReadNonEmpty()
        {
            while (true)
            {
                var s = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
                Console.Write("Please enter a value: ");
            }
        }

        private static string AskManualUrl(SupportedTypes type)
        {
            if (type == SupportedTypes.Da)
            {
                Console.WriteLine("Enter DA URL. Example:");
                Console.WriteLine("  opcda://localhost/Smar.LC700Server.1");
                Console.Write("DA URL: ");
            }
            else
            {
                Console.WriteLine("Enter UA URL. Example:");
                Console.WriteLine("  opc.tcp://host:port/endpoint");
                Console.Write("UA URL: ");
            }
            return ReadNonEmpty();
        }

        private static SupportedTypes AskSupportedType()
        {
            while (true)
            {
                Console.Write("Type (Ua/Da): ");
                var t = Console.ReadLine();
                if (Enum.TryParse<SupportedTypes>(t ?? "", true, out var parsed))
                    return parsed;
                Console.WriteLine("Invalid type.");
            }
        }

        private static SupportedTypes GetOpcType(string s)
        {
            if (!Enum.TryParse(s, true, out SupportedTypes result))
                throw new ArgumentException("Type not supported");
            return result;
        }

        private static string GetSupportedTypes()
        {
            return string.Join(", ", Enum.GetNames(typeof(SupportedTypes)));
        }

        private static IClient<Node> GetClient(string url, SupportedTypes type)
        {
            switch (type)
            {
                case SupportedTypes.Ua: return new UaClient(new Uri(url));
                case SupportedTypes.Da: return new DaClient(new Uri(url));
                default: throw new ArgumentOutOfRangeException("type");
            }
        }
    }

    public enum SupportedTypes
    {
        Ua,
        Da
    }

    // Nenhuma definição de interface COM é mais necessária aqui
}