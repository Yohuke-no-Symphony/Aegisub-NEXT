namespace AegiNext.SubtitleFormats;

internal static class AssOverrideTags
{
    internal static readonly string[] KnownNames = ["iclip", "alpha", "xbord", "ybord", "xshad", "yshad", "fscx", "fscy", "bord", "shad", "blur", "move", "clip", "fade", "pos", "fad", "frz", "frx", "fry", "fsp", "fax", "fay", "org", "pbo", "fn", "fs", "fe", "fr", "be", "an", "kf", "ko", "kt", "1c", "2c", "3c", "4c", "1a", "2a", "3a", "4a", "b", "i", "u", "s", "c", "r", "k", "K", "p", "q", "t", "a"];

    internal static IEnumerable<AssOverrideTag> Parse(string block)
    {
        for (var cursor = 0; cursor < block.Length;)
        {
            if (block[cursor] != '\\')
            {
                cursor++;
                continue;
            }
            var start = cursor++;
            var nameStart = cursor;
            var name = KnownNames.FirstOrDefault(candidate => block.AsSpan(nameStart).StartsWith(candidate, StringComparison.Ordinal));
            if (name is not null)
            {
                cursor += name.Length;
            }
            else
            {
                while (cursor < block.Length && char.IsAsciiLetterOrDigit(block[cursor]))
                {
                    cursor++;
                }
                name = block[nameStart..cursor];
            }
            var valueStart = cursor;
            var depth = 0;
            while (cursor < block.Length && (block[cursor] != '\\' || depth > 0))
            {
                depth += block[cursor] == '(' ? 1 : block[cursor] == ')' ? -1 : 0;
                if (depth < 0)
                {
                    throw new InvalidDataException("ASS 标签括号不匹配。");
                }
                cursor++;
            }
            if (depth != 0)
            {
                throw new InvalidDataException("ASS 标签括号不匹配。");
            }
            yield return new(name, block[valueStart..cursor].Trim(), start, cursor - start);
        }
    }

    internal static string[] Arguments(string value)
    {
        if (!value.StartsWith('(') || !value.EndsWith(')'))
        {
            throw new InvalidDataException("ASS 裁切和变换必须使用括号参数。");
        }
        var result = new List<string>();
        var start = 1;
        var depth = 0;
        for (var index = 1; index < value.Length - 1; index++)
        {
            depth += value[index] == '(' ? 1 : value[index] == ')' ? -1 : 0;
            if (value[index] == ',' && depth == 0)
            {
                result.Add(value[start..index].Trim());
                start = index + 1;
            }
        }
        result.Add(value[start..^1].Trim());
        return result.ToArray();
    }

    internal static string MaskIdentity(string source)
    {
        var values = new List<string>();
        for (var cursor = 0; cursor < source.Length; cursor++)
        {
            if (source[cursor] == '\\' && cursor + 1 < source.Length && source[cursor + 1] is '{' or '}')
            {
                cursor++;
                continue;
            }
            if (source[cursor] != '{')
            {
                continue;
            }
            var end = source.IndexOf('}', cursor + 1);
            if (end < 0)
            {
                throw new InvalidDataException("ASS 标签块缺少结束括号。");
            }
            foreach (var tag in Parse(source[(cursor + 1)..end]))
            {
                if (tag.Name is "clip" or "iclip")
                {
                    values.Add(tag.Name + tag.Value);
                }
                else if (tag.Name == "t")
                {
                    var arguments = Arguments(tag.Value);
                    var masks = Parse(arguments[^1]).Where(item => item.Name is "clip" or "iclip")
                        .Select(item => "\\" + item.Name + item.Value).ToArray();
                    if (masks.Length > 0)
                    {
                        values.Add("t(" + string.Join(',', arguments[..^1]) + "," + string.Concat(masks) + ")");
                    }
                }
            }
            cursor = end;
        }
        return string.Join('\n', values);
    }
}
