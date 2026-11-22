namespace TORTools.Core.SpritePack.Atlas;

/// <summary>
/// Pixel rectangle used by the sprite sheet bin packer. Mirrors
/// SpriteForge's <c>internal/sprite/packer.go::rect</c> which in turn
/// mirrors the Modding Kit's <c>SpriteSheetRect</c>.
/// </summary>
sealed class Rect
{
    public int X, Y, W, H;

    public Rect(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }

    public int X2 => X + W;
    public int Y2 => Y + H;
    public bool IsValid => W > 0 && H > 0;

    /// <summary>
    /// Touching edges do not count as collision (mirrors
    /// <c>SpriteSheetRect.IsCollide</c>).
    /// </summary>
    public bool IsCollide(Rect other)
    {
        var x  = X        > other.X        ? other.X        : X;
        var x2 = X2       > other.X2       ? X2             : other.X2;
        var y  = Y        > other.Y        ? other.Y        : Y;
        var y2 = Y2       > other.Y2       ? Y2             : other.Y2;
        if (x2 - x < W + other.W) return y2 - y < H + other.H;
        return false;
    }

    public bool IsSubRectOf(Rect other) =>
        other.X <= X && other.X2 >= X2 && other.Y <= Y && other.Y2 >= Y2;
}

/// <summary>
/// Port of SpriteForge's bin packer (<c>internal/sprite/packer.go</c>),
/// which in turn mirrors the Modding Kit's <c>SpriteDataEditor</c> packing.
/// </summary>
public static class BinPacker
{
    /// <summary>
    /// <c>DoNormalPacking</c>: sort by area descending, lay out each part
    /// into the first sheet with room, grow by adding new sheets when the
    /// current ones are full. Over-sized parts (larger than a sheet) are
    /// skipped — the caller decides whether that's fatal.
    /// </summary>
    public static List<Sheet> PackNormal(SpriteCategory category, int sheetW, int sheetH, int edge)
    {
        var sorted = SortPartsByArea(category.Parts);
        var sheets = new List<Sheet> { NewSheet(sheetW, sheetH, edge) };
        var sheetCount = 1;

        foreach (var part in sorted)
        {
            if (IsOversize(part, sheetW, sheetH)) continue;

            var placed = false;
            var sheetId = 1;
            foreach (var sheet in sheets)
            {
                if (AddSpritePart(sheet, part, edge))
                {
                    part.SheetId = sheetId;
                    placed = true;
                    break;
                }
                sheetId++;
            }

            if (!placed)
            {
                var sheet = NewSheet(sheetW, sheetH, edge);
                sheets.Add(sheet);
                // Can fail for near-oversize parts; generator ignores that.
                AddSpritePart(sheet, part, edge);
                sheetCount++;
                part.SheetId = sheetCount;
            }
        }
        return sheets;
    }

    /// <summary>
    /// <c>DoUniquePacking</c>: one sheet per part, sized exactly to the
    /// part. Used when <c>&lt;PackAllSpritesToUniqueTextures/&gt;</c> is
    /// set on the category.
    /// </summary>
    public static List<Sheet> PackUnique(SpriteCategory category)
    {
        var sorted = SortPartsByArea(category.Parts);
        var sheets = new List<Sheet>(sorted.Count);
        for (var i = 0; i < sorted.Count; i++)
        {
            var part = sorted[i];
            var sheet = NewSheet(part.Width, part.Height, 0);
            sheets.Add(sheet);
            AddSpritePart(sheet, part, 0);
            part.SheetId = i + 1;
        }
        return sheets;
    }

    /// <summary>
    /// Shrinks the sheet to the smallest power-of-two that fits every part
    /// (with 10px slack). Port of <c>SpriteSheet.RecalculateSize</c>.
    /// </summary>
    public static void RecalculateSize(Sheet sheet)
    {
        var maxX = 0;
        var maxY = 0;
        foreach (var part in sheet.Parts)
        {
            var right = part.SheetX + part.Width  + sheet.EdgeSize;
            var bottom = part.SheetY + part.Height + sheet.EdgeSize;
            if (right  > maxX) maxX = right;
            if (bottom > maxY) maxY = bottom;
        }
        var w = NextPowerOfTwo(maxX);
        var h = NextPowerOfTwo(maxY);
        if (sheet.Width  - w > 10) sheet.Width  = w;
        if (sheet.Height - h > 10) sheet.Height = h;
    }

    static Sheet NewSheet(int w, int h, int edge)
    {
        var s = new Sheet { Width = w, Height = h, EdgeSize = edge };
        FreeRects(s).Add(new Rect(0, 0, w, h));
        return s;
    }

    /// <summary>
    /// Stable sort by area descending (ties break by scan order).
    /// Mirrors <c>sortPartsByArea</c>.
    /// </summary>
    static List<SpritePart> SortPartsByArea(IReadOnlyList<SpritePart> parts)
    {
        var out_ = parts.ToList();
        out_.Sort((a, b) =>
        {
            // > is descending by area
            var areaA = (long)a.Width * a.Height;
            var areaB = (long)b.Width * b.Height;
            return areaB.CompareTo(areaA);
        });
        return out_;
    }

    static bool IsOversize(SpritePart part, int sheetW, int sheetH) =>
        part.Width >= sheetW || part.Height >= sheetH;

    static bool AddSpritePart(Sheet sheet, SpritePart part, int edge)
    {
        var num = edge * 2;
        var w3 = part.Width  + num;
        var h3 = part.Height + num;

        var free = FreeRects(sheet);
        free.Sort((a, b) => a.X.CompareTo(b.X));

        Rect? chosen = null;
        foreach (var r in free)
        {
            if (w3 <= r.W && h3 <= r.H) chosen = r; // last fitting wins
        }
        if (chosen is null) return false;

        part.SheetX = chosen.X + edge;
        part.SheetY = chosen.Y + edge;

        var newRects = new List<Rect>
        {
            new Rect(chosen.X + w3, chosen.Y,       chosen.W - w3, chosen.H),
            new Rect(chosen.X,      chosen.Y + h3,  chosen.W,       chosen.H - h3),
        };

        for (var i = 0; i < free.Count; i++)
        {
            if (ReferenceEquals(free[i], chosen)) { free.RemoveAt(i); break; }
        }

        var placed = new Rect(chosen.X, chosen.Y, w3, h3);
        sheet.Parts.Add(part);

        for (var j = 0; j < free.Count; j++)
        {
            var r = free[j];
            if (r.IsCollide(placed))
            {
                var top    = new Rect(r.X,       r.Y,       r.W,              placed.Y - r.Y);
                var bottom = new Rect(r.X,       placed.Y2, r.W,              r.Y2 - placed.Y2);
                var left   = new Rect(r.X,       r.Y,       placed.X - r.X,   r.H);
                var right  = new Rect(placed.X2, r.Y,       r.X2 - placed.X2, r.H);
                newRects.Add(bottom);
                newRects.Add(left);
                newRects.Add(top);
                newRects.Add(right);
                free.RemoveAt(j);
                j--;
            }
        }

        foreach (var r in newRects)
            if (r.IsValid) free.Add(r);

        // Drop free rects that are strict sub-rects of another.
        for (var l = 0; l < free.Count; l++)
        {
            var a = free[l];
            for (var m = 0; m < free.Count; m++)
            {
                if (l != m && a.IsSubRectOf(free[m]))
                {
                    free.RemoveAt(l);
                    l--;
                    break;
                }
            }
        }

        return true;
    }

    // --- Sheet free-rect storage ------------------------------------------
    //
    // `Sheet` is a public DTO that lives on the output model (it's what
    // the SpriteData.xml writer and atlas renderer consume). The free-rect
    // list is internal to the packer so we park it in a conditional
    // weak-table keyed by sheet identity.

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Sheet, List<Rect>> _freeRects = new();

    static List<Rect> FreeRects(Sheet sheet) =>
        _freeRects.GetValue(sheet, static _ => new List<Rect>());

    static int NextPowerOfTwo(int x)
    {
        // 32-bit arithmetic, matches SpriteSheet.GetNextPowerOfTwo exactly.
        unchecked
        {
            var v = (uint)x;
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            v++;
            return (int)v;
        }
    }
}
