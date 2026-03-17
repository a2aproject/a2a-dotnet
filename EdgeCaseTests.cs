using A2A;
using A2A.V0_3;
using A2A.V0_3Compat;
using System;
using System.Text.Json;

// Edge case 1: FilePart with neither Bytes nor Uri set
Console.WriteLine("=== Edge Case 1: FilePart with neither Bytes nor Uri ===");
try {
    var emptyFilePart = new A2A.V0_3.FilePart();
    var v1Part = V03TypeConverter.ToV1Part(emptyFilePart);
    Console.WriteLine(\Result ContentCase: {v1Part.ContentCase}\);
    Console.WriteLine(\Has Text: {v1Part.Text != null}\);
    Console.WriteLine(\Has Raw: {v1Part.Raw != null}\);
    Console.WriteLine(\Has Url: {v1Part.Url != null}\);
    Console.WriteLine(\Has Data: {v1Part.Data != null}\);
    Console.WriteLine(\SUCCESS: Empty FilePart converted to empty Part\);
} catch (Exception ex) {
    Console.WriteLine(\EXCEPTION: {ex.GetType().Name} - {ex.Message}\);
}

// Edge case 2: Invalid base64 in Bytes
Console.WriteLine(\"\\n=== Edge Case 2: Invalid base64 in Bytes ===\");
try {
    var invalidBase64 = new A2A.V0_3.FilePart {
        File = new FileContent(\"!!!NOT_VALID_BASE64!!!\") {
            MimeType = \"text/plain\"
        }
    };
    var v1Part = V03TypeConverter.ToV1Part(invalidBase64);
    Console.WriteLine(\UNEXPECTED: Converted successfully\);
} catch (Exception ex) {
    Console.WriteLine(\EXCEPTION: {ex.GetType().Name} - {ex.Message}\);
}

// Edge case 3: Empty byte array
Console.WriteLine(\"\\n=== Edge Case 3: Empty byte array ===\");
try {
    var emptyBytes = new byte[] { };
    var v1Part = A2A.Part.FromRaw(emptyBytes, \"application/octet-stream\", \"empty.bin\");
    var base64Empty = Convert.ToBase64String(emptyBytes);
    Console.WriteLine(\Base64 of empty array: '{base64Empty}' (length: {base64Empty.Length})\);
    var v03Part = V03TypeConverter.ToV03Part(v1Part);
    Console.WriteLine(\V0.3 FilePart.File.Bytes: '{(v03Part as A2A.V0_3.FilePart).File.Bytes}'\);
    Console.WriteLine(\SUCCESS: Empty byte array handled\);
} catch (Exception ex) {
    Console.WriteLine(\EXCEPTION: {ex.GetType().Name} - {ex.Message}\);
}

// Edge case 4: Invalid URL string
Console.WriteLine(\"\\n=== Edge Case 4: Invalid URL string ===\");
try {
    var invalidUrl = \"not://a/valid:url\";
    var v1Part = A2A.Part.FromUrl(invalidUrl, \"text/plain\");
    var v03Part = V03TypeConverter.ToV03Part(v1Part);
    Console.WriteLine(\UNEXPECTED: Converted successfully to Uri: {(v03Part as A2A.V0_3.FilePart).File.Uri}\);
} catch (Exception ex) {
    Console.WriteLine(\EXCEPTION: {ex.GetType().Name} - {ex.Message}\);
}

// Round-trip test
Console.WriteLine(\"\\n=== Round-trip Test: v1 → v0.3 → v1 ===\");
try {
    var originalBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
    var v1Part1 = A2A.Part.FromRaw(originalBytes, \"application/octet-stream\", \"data.bin\");
    var v03Part = V03TypeConverter.ToV03Part(v1Part1);
    var v1Part2 = V03TypeConverter.ToV1Part(v03Part);
    
    Console.WriteLine(\Original Raw: {BitConverter.ToString(v1Part1.Raw)}\);
    Console.WriteLine(\After roundtrip Raw: {BitConverter.ToString(v1Part2.Raw)}\);
    Console.WriteLine(\Match: {v1Part1.Raw.SequenceEqual(v1Part2.Raw)}\);
    Console.WriteLine(\SUCCESS: Roundtrip preserved data\);
} catch (Exception ex) {
    Console.WriteLine(\EXCEPTION: {ex.GetType().Name} - {ex.Message}\);
}
