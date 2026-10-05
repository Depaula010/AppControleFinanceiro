namespace MeuSecretario.Domain.Automation;

/// <summary>
/// Porte do <c>rapidfuzz</c> 3.14.3 (o que a produção usa, <c>requirements.txt:10</c>) para o que o casamento de conta fixa
/// precisa: <c>ratio</c>, <c>partial_ratio</c>, <c>token_sort_ratio</c>, <c>token_set_ratio</c>, os dois <c>partial_token_*</c>,
/// <c>WRatio</c> e <c>process.extractOne</c>. Sem pré-processamento (o <c>rapidfuzz</c> 3.x não aplica nenhum por padrão),
/// sobre pontos de código. Notas em 0 a 100. Validado contra a biblioteca real (<c>automation-fixtures.json</c>).
/// </summary>
public static class FuzzyScorer
{
    private const double UnbaseScale = 0.95;

    public static double Ratio(string a, string b) => NormalizedSimilarity(CodePoints(a), CodePoints(b)) * 100;

    public static double PartialRatio(string a, string b)
    {
        var s1 = CodePoints(a);
        var s2 = CodePoints(b);
        if (s1.Length == 0 || s2.Length == 0)
        {
            return s1.Length == s2.Length ? 100 : 0;
        }

        var (shorter, longer) = s1.Length <= s2.Length ? (s1, s2) : (s2, s1);
        var best = PartialSimilarity(shorter, longer);
        if (best != 1.0 && s1.Length == s2.Length)
        {
            best = Math.Max(best, PartialSimilarity(longer, shorter));
        }

        return best * 100;
    }

    public static double TokenSortRatio(string a, string b) => Ratio(SortedTokens(a), SortedTokens(b));

    public static double TokenSetRatio(string a, string b)
    {
        var tokensA = Tokens(a).ToHashSet(StringComparer.Ordinal);
        var tokensB = Tokens(b).ToHashSet(StringComparer.Ordinal);
        if (tokensA.Count == 0 || tokensB.Count == 0)
        {
            return 0;
        }

        var intersect = tokensA.Where(tokensB.Contains).ToList();
        var diffAb = tokensA.Where(t => !tokensB.Contains(t)).ToList();
        var diffBa = tokensB.Where(t => !tokensA.Contains(t)).ToList();
        if (intersect.Count > 0 && (diffAb.Count == 0 || diffBa.Count == 0))
        {
            return 100;
        }

        var joinedAb = Join(diffAb);
        var joinedBa = Join(diffBa);
        var abLen = CodePoints(joinedAb).Length;
        var baLen = CodePoints(joinedBa).Length;
        var sectLen = CodePoints(Join(intersect)).Length;
        var hasSect = sectLen > 0 ? 1 : 0;
        var sectAbLen = sectLen + hasSect + abLen;
        var sectBaLen = sectLen + hasSect + baLen;

        // sect+ab x sect+ba: o prefixo comum não muda a distância de Indel, só os tamanhos usados na normalização
        var result = NormalizedDistanceScore(Indel(CodePoints(joinedAb), CodePoints(joinedBa)), sectAbLen + sectBaLen);
        if (sectLen == 0)
        {
            return result;
        }

        var sectAb = NormalizedDistanceScore(hasSect + abLen, sectLen + sectAbLen);
        var sectBa = NormalizedDistanceScore(hasSect + baLen, sectLen + sectBaLen);
        return Math.Max(result, Math.Max(sectAb, sectBa));
    }

    public static double PartialTokenSortRatio(string a, string b) => PartialRatio(SortedTokens(a), SortedTokens(b));

    public static double PartialTokenSetRatio(string a, string b)
    {
        var tokensA = Tokens(a).ToHashSet(StringComparer.Ordinal);
        var tokensB = Tokens(b).ToHashSet(StringComparer.Ordinal);
        if (tokensA.Count == 0 || tokensB.Count == 0)
        {
            return 0;
        }

        if (tokensA.Overlaps(tokensB))
        {
            return 100;
        }

        return PartialRatio(Join(tokensA), Join(tokensB));
    }

    /// <summary><c>fuzz.WRatio</c>: combina <c>ratio</c> e as variantes por token com fatores de escala.</summary>
    public static double WRatio(string a, string b)
    {
        var len1 = CodePoints(a).Length;
        var len2 = CodePoints(b).Length;
        if (len1 == 0 || len2 == 0)
        {
            return 0;
        }

        var lenRatio = len1 > len2 ? (double)len1 / len2 : (double)len2 / len1;
        var endRatio = Ratio(a, b);
        if (lenRatio < 1.5)
        {
            return Math.Max(endRatio, Math.Max(TokenSetRatio(a, b), TokenSortRatio(a, b)) * UnbaseScale);
        }

        var partialScale = lenRatio <= 8.0 ? 0.9 : 0.6;
        endRatio = Math.Max(endRatio, PartialRatio(a, b) * partialScale);
        return Math.Max(endRatio, PartialTokenRatio(a, b) * UnbaseScale * partialScale);
    }

    /// <summary>
    /// <c>process.extractOne(query, choices, scorer=WRatio, score_cutoff=cutoff)</c>: o primeiro com a maior nota que
    /// alcança o corte, ou <c>null</c>.
    /// </summary>
    public static FuzzyMatch? ExtractOne(string query, IEnumerable<string> choices, double cutoff)
    {
        FuzzyMatch? best = null;
        foreach (var choice in choices)
        {
            var score = WRatio(query, choice);
            if (score >= cutoff && (best is null || score > best.Score))
            {
                best = new FuzzyMatch(choice, score);
                if (score >= 100)
                {
                    break;
                }
            }
        }

        return best;
    }

    private static double PartialTokenRatio(string a, string b)
    {
        var splitA = Tokens(a).ToList();
        var splitB = Tokens(b).ToList();
        var setA = splitA.ToHashSet(StringComparer.Ordinal);
        var setB = splitB.ToHashSet(StringComparer.Ordinal);
        if (setA.Overlaps(setB))
        {
            return 100;
        }

        // sem palavra em comum, as diferenças são os próprios conjuntos: só difere de "ordenado com repetições" se há repetidas
        var result = PartialRatio(Join(splitA), Join(splitB));
        if (splitA.Count == setA.Count && splitB.Count == setB.Count)
        {
            return result;
        }

        return Math.Max(result, PartialRatio(Join(setA), Join(setB)));
    }

    private static string SortedTokens(string text) => Join(Tokens(text));

    private static string Join(IEnumerable<string> tokens)
    {
        var sorted = tokens.ToList();
        sorted.Sort(StringComparer.Ordinal);
        return string.Join(' ', sorted);
    }

    /// <summary><c>str.split()</c>: separa por qualquer espaço Unicode e descarta vazios.</summary>
    private static string[] Tokens(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static double PartialSimilarity(int[] needle, int[] haystack)
    {
        var chars = needle.ToHashSet();
        var len1 = needle.Length;
        var len2 = haystack.Length;
        var best = 0.0;

        for (var i = 1; i < len1; i++)
        {
            if (!chars.Contains(haystack[i - 1]))
            {
                continue;
            }

            var similarity = NormalizedSimilarity(needle, haystack.AsSpan(0, i));
            if (similarity > best)
            {
                best = similarity;
                if (best == 1.0)
                {
                    return best;
                }
            }
        }

        for (var i = 0; i < len2 - len1; i++)
        {
            if (!chars.Contains(haystack[i + len1 - 1]))
            {
                continue;
            }

            var similarity = NormalizedSimilarity(needle, haystack.AsSpan(i, len1));
            if (similarity > best)
            {
                best = similarity;
                if (best == 1.0)
                {
                    return best;
                }
            }
        }

        for (var i = len2 - len1; i < len2; i++)
        {
            if (!chars.Contains(haystack[i]))
            {
                continue;
            }

            var similarity = NormalizedSimilarity(needle, haystack.AsSpan(i));
            if (similarity > best)
            {
                best = similarity;
                if (best == 1.0)
                {
                    return best;
                }
            }
        }

        return best;
    }

    private static double NormalizedSimilarity(ReadOnlySpan<int> a, ReadOnlySpan<int> b)
    {
        var maximum = a.Length + b.Length;
        var distance = Indel(a, b);
        var normalizedDistance = maximum == 0 ? 0.0 : (double)distance / maximum;
        return 1.0 - normalizedDistance;
    }

    private static double NormalizedDistanceScore(int distance, int lengthSum)
    {
        var normalizedDistance = lengthSum == 0 ? 0.0 : (double)distance / lengthSum;
        return 100.0 * (1.0 - normalizedDistance);
    }

    /// <summary>Distância de Indel: <c>len(a) + len(b) - 2 * LCS</c>.</summary>
    private static int Indel(ReadOnlySpan<int> a, ReadOnlySpan<int> b) => a.Length + b.Length - (2 * Lcs(a, b));

    private static int Lcs(ReadOnlySpan<int> a, ReadOnlySpan<int> b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                current[j] = a[i - 1] == b[j - 1]
                    ? previous[j - 1] + 1
                    : Math.Max(previous[j], current[j - 1]);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private static int[] CodePoints(string text)
    {
        var list = new List<int>(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            list.Add(rune.Value);
        }

        return [.. list];
    }
}

/// <summary>Melhor escolha do <c>extractOne</c>.</summary>
public sealed record FuzzyMatch(string Choice, double Score);
