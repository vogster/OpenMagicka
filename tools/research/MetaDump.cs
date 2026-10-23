using System.Reflection.Metadata; using System.Reflection.PortableExecutable;
var path=args[0]; using var fs=File.OpenRead(path); using var pe=new PEReader(fs); var md=pe.GetMetadataReader();
string Name(EntityHandle h){ if(h.IsNil)return ""; switch(h.Kind){case HandleKind.TypeDefinition:{var t=md.GetTypeDefinition((TypeDefinitionHandle)h);return md.GetString(t.Namespace)+"."+md.GetString(t.Name);}case HandleKind.TypeReference:{var t=md.GetTypeReference((TypeReferenceHandle)h);return md.GetString(t.Namespace)+"."+md.GetString(t.Name);}case HandleKind.TypeSpecification:{var s=md.GetTypeSpecification((TypeSpecificationHandle)h);var b=md.GetBlobReader(s.Signature);b.ReadByte();if(b.ReadByte()==0x15){} return "spec";} } return "?";}
Console.WriteLine("== AssemblyRefs");
foreach(var r in md.AssemblyReferences){var a=md.GetAssemblyReference(r);Console.WriteLine($"{md.GetString(a.Name)} {a.Version}");}
Console.WriteLine("== ModuleRefs/PInvoke");
foreach(var m in md.MethodDefinitions){var d=md.GetMethodDefinition(m);var imp=d.GetImport();if(!imp.Module.IsNil)Console.WriteLine(md.GetString(md.GetModuleReference(imp.Module).Name)+"!"+md.GetString(imp.Name));}
var ns=new Dictionary<string,List<string>>();
foreach(var h in md.TypeDefinitions){var t=md.GetTypeDefinition(h);if(t.GetDeclaringType().IsNil==false)continue;var n=md.GetString(t.Namespace);if(!ns.ContainsKey(n))ns[n]=new();var bt=t.BaseType.IsNil?"":Name(t.BaseType);ns[n].Add(md.GetString(t.Name)+(bt.Length>0?" : "+bt:""));}
Console.WriteLine("== Namespaces");
foreach(var kv in ns.OrderBy(k=>k.Key))Console.WriteLine($"{kv.Key} ({kv.Value.Count})");
if(args.Length>1){foreach(var kv in ns.OrderBy(k=>k.Key)){Console.WriteLine("### "+kv.Key);foreach(var x in kv.Value.OrderBy(x=>x))Console.WriteLine("  "+x);}}
