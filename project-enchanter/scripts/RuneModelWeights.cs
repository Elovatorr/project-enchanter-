using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ProjectEnchanter;

/// <summary>
/// Trained rune-model parameters, loaded from the CSV files in
/// resources/rune_model/ (written by tools/train_rune_model.py).
///
/// The CSVs are embedded into the assembly at build time, so loading works the
/// same in the game, in tests, and in exported builds (no res:// file access).
/// To tweak the model by hand, edit the CSVs and rebuild — no code generation.
/// </summary>
public static class RuneModelWeights
{
    private const string ResourceFolder = "rune_model";

    public static readonly int InputSize;
    public static readonly int HiddenSize;
    public static readonly int OutputSize;
    public static readonly string[] OutputNames;

    /// <summary>Weights of the hidden layer: HiddenSize rows × InputSize columns.</summary>
    public static readonly float[] W1;

    /// <summary>Hidden-layer biases: HiddenSize values.</summary>
    public static readonly float[] B1;

    /// <summary>Output-layer weights: OutputSize rows × HiddenSize columns.</summary>
    public static readonly float[] W2;

    /// <summary>Output-layer biases: OutputSize values.</summary>
    public static readonly float[] B2;

    /// <summary>Per-feature mean used to standardize inputs before the network.</summary>
    public static readonly float[] FeatureMean;

    /// <summary>Per-feature standard deviation used to standardize inputs.</summary>
    public static readonly float[] FeatureStd;

    // ---- Parity sample (the tests use it to detect C#/Python drift) ----

    public static readonly int[] ParityStrokeLengths;
    public static readonly float[] ParityPoints;
    public static readonly float[] ParityFeatures;

    static RuneModelWeights()
    {
        Dictionary<string, string> meta = LoadMeta();

        InputSize = int.Parse(Get(meta, "input_size"), CultureInfo.InvariantCulture);
        HiddenSize = int.Parse(Get(meta, "hidden_size"), CultureInfo.InvariantCulture);
        OutputSize = int.Parse(Get(meta, "output_size"), CultureInfo.InvariantCulture);
        OutputNames = Get(meta, "output_names").Split(';', StringSplitOptions.RemoveEmptyEntries);

        W1 = LoadMatrix("w1.csv", HiddenSize, InputSize);
        B1 = LoadMatrix("b1.csv", HiddenSize, 1);
        W2 = LoadMatrix("w2.csv", OutputSize, HiddenSize);
        B2 = LoadMatrix("b2.csv", OutputSize, 1);
        FeatureMean = LoadMatrix("feature_mean.csv", 1, InputSize);
        FeatureStd = LoadMatrix("feature_std.csv", 1, InputSize);

        ParityStrokeLengths = Get(meta, "parity_stroke_lengths")
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => int.Parse(v, CultureInfo.InvariantCulture))
            .ToArray();
        ParityPoints = LoadMatrix("parity_points.csv", ParityStrokeLengths.Sum(), 2);
        ParityFeatures = LoadMatrix("parity_features.csv", 1, InputSize);

        if (OutputNames.Length != OutputSize)
        {
            throw new InvalidOperationException(
                $"rune_model/meta.csv: expected {OutputSize} output names, got {OutputNames.Length}.");
        }
        if (FeatureMean.Length != InputSize || FeatureStd.Length != InputSize)
        {
            throw new InvalidOperationException("rune_model: feature_mean/feature_std size mismatch.");
        }
        foreach (float std in FeatureStd)
        {
            if (std <= 0f)
            {
                throw new InvalidOperationException("rune_model/feature_std.csv must be positive.");
            }
        }
    }

    private static Dictionary<string, string> LoadMeta()
    {
        Dictionary<string, string> meta = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in ReadResource("meta.csv"))
        {
            string[] parts = line.Split(',', 2);
            if (parts.Length == 2) meta[parts[0].Trim()] = parts[1].Trim();
        }
        return meta;
    }

    private static string Get(Dictionary<string, string> meta, string key)
        => meta.TryGetValue(key, out string? value)
            ? value
            : throw new InvalidOperationException($"rune_model/meta.csv is missing '{key}'.");

    /// <summary>Loads a numeric CSV as a row-major matrix of the expected shape.</summary>
    private static float[] LoadMatrix(string fileName, int expectedRows, int expectedColumns)
    {
        List<float> values = [];
        int row = 0;
        foreach (string line in ReadResource(fileName))
        {
            string[] cells = line.Split(',');
            if (cells.Length != expectedColumns)
            {
                throw new InvalidOperationException(
                    $"rune_model/{fileName} row {row}: expected {expectedColumns} columns, got {cells.Length}.");
            }
            foreach (string cell in cells)
            {
                values.Add(float.Parse(cell, NumberStyles.Float, CultureInfo.InvariantCulture));
            }
            row++;
        }

        if (row != expectedRows)
        {
            throw new InvalidOperationException(
                $"rune_model/{fileName}: expected {expectedRows} rows, got {row}.");
        }
        return [.. values];
    }

    private static IEnumerable<string> ReadResource(string fileName)
    {
        Assembly assembly = typeof(RuneModelWeights).Assembly;
        string suffix = $".{ResourceFolder}.{fileName}";
        string? resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.Ordinal));

        if (resourceName is null)
        {
            throw new InvalidOperationException(
                $"Embedded resource '{suffix}' not found. Ensure resources\\{ResourceFolder}\\*.csv " +
                "is included as EmbeddedResource in the csproj and that the model was trained.");
        }

        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Cannot open embedded resource '{resourceName}'.");
        using StreamReader reader = new(stream);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            line = line.Trim();
            if (line.Length > 0) yield return line;
        }
    }
}
