using AddressablesTools;
using AddressablesTools.Catalog;
using AddressablesTools.Classes;
using AssetsTools.NET;
using System.Text;

if (args.Length == 2)
{
    ConvertExample(args[0], args[1]);
}
else if (args.Length == 0)
{
    PrintHelp();
}
else if (args[0] == "searchasset")
{
    SearchExample(args);
}
else if (args[0] == "patchcrc")
{
    PatchCrcExample(args);
}
else
{
    Console.WriteLine("Error: Argumentos insuficientes o modo no soportado.");
    PrintHelp();
}

static void PrintHelp()
{
    Console.WriteLine("Uso fácil:");
    Console.WriteLine("  BinToJson.exe <input> <output>");
    Console.WriteLine("  Ejemplo: BinToJson.exe catalog.bin catalog.json");
    Console.WriteLine("\nOtros modos:");
    Console.WriteLine("  BinToJson.exe searchasset <file>");
    Console.WriteLine("  BinToJson.exe patchcrc <file>");
}

static void ConvertExample(string inputPath, string outputPath)
{
    if (!File.Exists(inputPath))
    {
        Console.WriteLine($"Error: El archivo de entrada '{inputPath}' no existe.");
        return;
    }

    ContentCatalogData ccd;
    bool fromBundle = IsUnityFS(inputPath);

    if (fromBundle)
    {
        ccd = AddressablesCatalogFileParser.FromBundle(inputPath);
    }
    else
    {
        CatalogFileType fileType;
        using (FileStream fs = File.OpenRead(inputPath))
        {
            fileType = AddressablesCatalogFileParser.GetCatalogFileType(fs);
        }

        if (fileType == CatalogFileType.Json)
        {
            ccd = AddressablesCatalogFileParser.FromJsonString(File.ReadAllText(inputPath));
        }
        else if (fileType == CatalogFileType.Binary)
        {
            ccd = AddressablesCatalogFileParser.FromBinaryData(File.ReadAllBytes(inputPath));
        }
        else
        {
            Console.WriteLine("Error: El archivo de entrada no es un catálogo válido (.bin o .json).");
            return;
        }
    }

    string ext = Path.GetExtension(outputPath).ToLower();

    try
    {
        if (ext == ".json")
        {
            File.WriteAllText(outputPath, AddressablesCatalogFileParser.ToJsonString(ccd));
        }
        else if (ext == ".bin")
        {
            File.WriteAllBytes(outputPath, AddressablesCatalogFileParser.ToBinaryData(ccd));
        }
        else if (ext == ".bundle" || ext == ".patched") // patched for compatibility with old example
        {
             if (fromBundle)
             {
                 AddressablesCatalogFileParser.ToBundle(ccd, inputPath, outputPath);
             }
             else
             {
                 Console.WriteLine("Error: Para exportar a .bundle, el origen también debe ser un .bundle.");
                 return;
             }
        }
        else
        {
            Console.WriteLine($"Error: Extensión de salida '{ext}' no soportada. Usa .json o .bin");
            return;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error durante la conversión: {ex.Message}");
    }
}

static void SearchExample(string[] args)
{
    if (args.Length < 2)
    {
        Console.WriteLine("Uso: searchasset <path_to_catalog>");
        return;
    }

    bool fromBundle = IsUnityFS(args[1]);

    ContentCatalogData ccd;
    if (fromBundle)
    {
        ccd = AddressablesCatalogFileParser.FromBundle(args[1]);
    }
    else
    {
        CatalogFileType fileType;
        using (FileStream fs = File.OpenRead(args[1]))
        {
            fileType = AddressablesCatalogFileParser.GetCatalogFileType(fs);
        }

        if (fileType == CatalogFileType.Json)
        {
            ccd = AddressablesCatalogFileParser.FromJsonString(File.ReadAllText(args[1]));
        }
        else if (fileType == CatalogFileType.Binary)
        {
            ccd = AddressablesCatalogFileParser.FromBinaryData(File.ReadAllBytes(args[1]));
        }
        else
        {
            Console.WriteLine("not a valid catalog file");
            return;
        }
    }

    Console.Write("search key to find bundles of: ");
    string? search = Console.ReadLine();

    if (search == null)
    {
        return;
    }

    search = search.ToLower();
    foreach (object k in ccd.Resources.Keys)
    {
        if (k is string s && s.ToLower().Contains(search))
        {
            Console.Write(s);
            var rsrcs = ccd.Resources[s];
            foreach (var rsrc in rsrcs)
            {
                Console.WriteLine($" (id: {rsrc.InternalId}, prov: {rsrc.ProviderId})");
                if (rsrc.ProviderId == "UnityEngine.ResourceManagement.ResourceProviders.AssetBundleProvider")
                {
                    var data = rsrc.Data;
                    if (data is WrappedSerializedObject { Object: AssetBundleRequestOptions abro })
                    {
                        uint crc = abro.Crc;
                        Console.WriteLine($"  crc = {crc:x8}");
                    }
                }
                else if (rsrc.ProviderId == "UnityEngine.ResourceManagement.ResourceProviders.BundledAssetProvider")
                {
                    List<ResourceLocation> locs;
                    if (rsrc.Dependencies != null)
                    {
                        // new version
                        locs = rsrc.Dependencies;
                    }
                    else if (rsrc.DependencyKey != null)
                    {
                        // old version
                        locs = ccd.Resources[rsrc.DependencyKey];
                    }
                    else
                    {
                        continue;
                    }

                    Console.WriteLine($"  {locs[0].InternalId}");
                    if (locs.Count > 1)
                    {
                        for (int i = 1; i < locs.Count; i++)
                        {
                            Console.WriteLine($"    {locs[i].InternalId}");
                        }
                    }
                }
            }
        }
    }
}

static bool IsUnityFS(string path)
{
    const string unityFs = "UnityFS";
    if (!File.Exists(path)) return false;
    using AssetsFileReader reader = new AssetsFileReader(path);
    if (reader.BaseStream.Length < unityFs.Length)
    {
        return false;
    }

    return reader.ReadStringLength(unityFs.Length) == unityFs;
}

static void PatchCrcRecursive(ResourceLocation thisRsrc, HashSet<ResourceLocation> seenRsrcs)
{
    if (seenRsrcs.Contains(thisRsrc))
        return;

    var data = thisRsrc.Data;
    if (data is WrappedSerializedObject { Object: AssetBundleRequestOptions abro })
    {
        abro.Crc = 0;
    }

    seenRsrcs.Add(thisRsrc);
    foreach (var childRsrc in thisRsrc.Dependencies)
    {
        PatchCrcRecursive(childRsrc, seenRsrcs);
    }
}

static void PatchCrcExample(string[] args)
{
    if (args.Length < 2)
    {
        Console.WriteLine("Uso: patchcrc <path_to_catalog>");
        return;
    }

    bool fromBundle = IsUnityFS(args[1]);

    ContentCatalogData ccd;
    CatalogFileType fileType = CatalogFileType.None;
    if (fromBundle)
    {
        ccd = AddressablesCatalogFileParser.FromBundle(args[1]);
    }
    else
    {
        using (FileStream fs = File.OpenRead(args[1]))
        {
            fileType = AddressablesCatalogFileParser.GetCatalogFileType(fs);
        }

        switch (fileType)
        {
            case CatalogFileType.Json:
                ccd = AddressablesCatalogFileParser.FromJsonString(File.ReadAllText(args[1]));
                break;
            case CatalogFileType.Binary:
                ccd = AddressablesCatalogFileParser.FromBinaryData(File.ReadAllBytes(args[1]));
                break;
            default:
                Console.WriteLine("not a valid catalog file");
                return;
        }
    }

    Console.WriteLine("patching...");

    var seenRsrcs = new HashSet<ResourceLocation>();
    foreach (var resourceList in ccd.Resources.Values)
    {
        foreach (var rsrc in resourceList)
        {
            if (rsrc.Dependencies != null)
            {
                PatchCrcRecursive(rsrc, seenRsrcs);
                continue;
            }

            if (rsrc.ProviderId == "UnityEngine.ResourceManagement.ResourceProviders.AssetBundleProvider")
            {
                var data = rsrc.Data;
                if (data is WrappedSerializedObject { Object: AssetBundleRequestOptions abro })
                {
                    abro.Crc = 0;
                }
            }
        }
    }

    if (fromBundle)
    {
        AddressablesCatalogFileParser.ToBundle(ccd, args[1], args[1] + ".patched");
    }
    else
    {
        switch (fileType)
        {
            case CatalogFileType.Json:
                File.WriteAllText(args[1] + ".patched", AddressablesCatalogFileParser.ToJsonString(ccd));
                break;
            case CatalogFileType.Binary:
                File.WriteAllBytes(args[1] + ".patched", AddressablesCatalogFileParser.ToBinaryData(ccd));
                break;
            default:
                return;
        }
    }

    File.Move(args[1], args[1] + ".old", true);
    File.Move(args[1] + ".patched", args[1], true);
}
