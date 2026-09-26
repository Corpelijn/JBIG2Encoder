// Ported from jbig2enc (jbig2enc.cc: jbig2enc_auto_threshold, jbig2enc_auto_threshold_using_hash,
// unite_templates, remove_templates)
// Copyright 2006 Google Inc. Licensed under the Apache License, Version 2.0.
// See the NOTICE file at the repository root.

using System;
using System.Collections.Generic;
using System.Linq;

namespace JBIG2Encoder.Symbols;

/// <summary>
/// "Automatic thresholding": after classification, merges exemplars that <see cref="SymbolComparator"/> considers
/// equivalent, which shrinks the symbol dictionary. The classification threshold itself is not changed.
/// </summary>
internal static class TemplateMerger
{
    /// <summary>All-pairs comparison, exactly as jbig2enc_auto_threshold. Quadratic in the number of symbols.</summary>
    public static void MergeAllPairs(CorrelationClassifier classifier)
    {
        List<SymbolTemplate> templates = classifier.Templates;
        List<int> componentClass = classifier.ComponentClass;

        for (int i = 0; i < templates.Count; i++)
        {
            // Looking only forward is enough because the comparison is symmetric.
            for (int j = i + 1; j < templates.Count; j++)
            {
                if (!SymbolComparator.AreEquivalent(templates[i].Bordered, templates[j].Bordered)) continue;

                // Everything that used class j now uses class i...
                for (int c = 0; c < componentClass.Count; c++)
                {
                    if (componentClass[c] == j) componentClass[c] = i;
                }

                // ...and the last exemplar takes over the vacated slot j.
                int last = templates.Count - 1;
                if (last != j)
                {
                    templates[j] = templates[last];
                    for (int c = 0; c < componentClass.Count; c++)
                    {
                        if (componentClass[c] == last) componentClass[c] = j;
                    }
                }
                templates.RemoveAt(last);

                j--; // the moved exemplar has not been compared with i yet
            }
        }
    }

    /// <summary>
    /// Like <see cref="MergeAllPairs"/> but only compares exemplars that share a cheap hash (size and number of
    /// 4-connected pieces), as jbig2enc_auto_threshold_using_hash.
    /// </summary>
    public static void MergeUsingHash(CorrelationClassifier classifier)
    {
        List<SymbolTemplate> templates = classifier.Templates;
        List<int> componentClass = classifier.ComponentClass;

        var bins = new SortedDictionary<uint, List<int>>();
        for (int i = 0; i < templates.Count; i++)
        {
            BinaryBitmap bordered = templates[i].Bordered;
            int pieces = ConnectedComponents.Count(bordered, eightConnected: false);
            uint hash = unchecked((uint)pieces + 10u * (uint)bordered.Height + 10000u * (uint)bordered.Width) % 10000000u;
            if (!bins.TryGetValue(hash, out List<int>? bin)) bins[hash] = bin = new List<int>();
            bin.Add(i);
        }

        // representative -> exemplars that are replaced by it
        var replacements = new SortedDictionary<int, List<int>>();
        foreach (List<int> bin in bins.Values)
        {
            for (int a = 0; a < bin.Count; a++)
            {
                int first = bin[a];
                List<int>? duplicates = null;
                for (int b = a + 1; b < bin.Count;)
                {
                    if (SymbolComparator.AreEquivalent(templates[first].Bordered, templates[bin[b]].Bordered))
                    {
                        (duplicates ??= new List<int>()).Add(bin[b]);
                        bin.RemoveAt(b);
                    }
                    else
                    {
                        b++;
                    }
                }
                if (duplicates != null) replacements[first] = duplicates;
            }
        }

        if (replacements.Count == 0) return;

        // Redirect components of every removed exemplar to its representative.
        var target = new int[templates.Count];
        for (int i = 0; i < target.Length; i++) target[i] = i;
        var removed = new bool[templates.Count];
        foreach ((int representative, List<int> duplicates) in replacements)
        {
            foreach (int duplicate in duplicates)
            {
                target[duplicate] = representative;
                removed[duplicate] = true;
            }
        }

        // Compact the exemplar list, keeping the original order of the survivors.
        var newIndex = new int[templates.Count];
        int next = 0;
        for (int i = 0; i < templates.Count; i++) newIndex[i] = removed[i] ? -1 : next++;

        for (int c = 0; c < componentClass.Count; c++) componentClass[c] = newIndex[target[componentClass[c]]];

        List<SymbolTemplate> survivors = templates.Where((_, i) => !removed[i]).ToList();
        templates.Clear();
        templates.AddRange(survivors);
    }
}
