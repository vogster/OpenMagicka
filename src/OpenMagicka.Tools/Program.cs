using System.Text;

// Loc strings are in many scripts; the console's legacy code page would turn them into '?'.
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
return OpenMagicka.Tools.Omx.Run(args, Console.Out, Console.Error);
