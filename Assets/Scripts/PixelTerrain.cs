using System.Collections.Generic;
using UnityEngine;

// A tile-aligned scrolling strip. All positions and road ports use bottom-up rows.
public sealed class PixelTerrain : MonoBehaviour
{
    const int Width = 11, Period = 64, VisibleRows = 22;
    readonly int[,] layout = new int[Period, Width];
    readonly Dictionary<int, Sprite> art = new Dictionary<int, Sprite>();
    readonly Transform[] rows = new Transform[VisibleRows];
    readonly SpriteRenderer[,] cells = new SpriteRenderer[VisibleRows, Width];
    readonly int[] sourceRows = new int[VisibleRows];
    double distance;

    public void Initialize()
    {
        BuildLayout();
        for (int r = 0; r < Period; r++)
        for (int c = 0; c < Width; c++)
        {
            int id = layout[r, c];
            if (art.ContainsKey(id)) continue;
            var texture = Resources.Load<Texture2D>("Art/map_" + id);
            if (texture == null) throw new System.InvalidOperationException("Missing map tile " + id);
            texture.filterMode = FilterMode.Point;
            art[id] = Sprite.Create(texture, new Rect(0, 0, 16, 16), Vector2.one * .5f, 16, 0, SpriteMeshType.FullRect);
        }
        for (int r = 0; r < VisibleRows; r++)
        {
            rows[r] = new GameObject("Terrain row " + r).transform;
            rows[r].SetParent(transform, false);
            for (int c = 0; c < Width; c++)
            {
                var tile = new GameObject("Ground tile");
                tile.transform.SetParent(rows[r], false);
                tile.transform.localPosition = new Vector3(c - 5, 0, 0);
                var renderer = tile.AddComponent<SpriteRenderer>();
                renderer.sortingOrder = -35;
                renderer.color = new Color(.62f, .68f, .60f);
                cells[r, c] = renderer;
            }
        }
        ResetScroll();
    }

    void BuildLayout()
    {
        for (int y = 0; y < Period; y++)
        for (int x = 0; x < Width; x++)
        {
            int hash = (x * 37 + y * 19 + x * y * 3) % 101;
            layout[y, x] = hash < 50 ? 50 : 110;
            // Forest clusters sit away from the flight corridor and road shoulders.
            if ((x <= 1 || x >= 6) && hash < 28)
                layout[y, x] = hash % 3 == 0 ? 36 : hash % 3 == 1 ? 48 : 60;
            if (x == 3) layout[y, x] = y % 4 < 2 ? 85 : 87;
        }
        Coast(7, 19);
        Coast(38, 52);
        layout[13, 9] = 64; // enclosed island surrounded by water, never a road
        layout[45, 9] = 65;
        for (int x = 0; x < 3; x++) layout[10, x] = 74;
        layout[10, 3] = 77; // north/south/west junction
        for (int x = 4; x < 6; x++) layout[29, x] = 98;
        layout[29, 3] = 76; // north/south/east junction
        layout[29, 6] = 113; // west-connected dead end
        layout[8, 1] = 72;
        layout[12, 1] = 84;
        layout[27, 5] = 72;
        layout[31, 5] = 84;
        layout[29, 7] = 96;
        layout[33, 6] = 111;
        for (int x = 0; x < 3; x++) layout[57, x] = 74;
        layout[57, 3] = 77;
        layout[55, 1] = 84;
        // Additional side routes. Retiling derives every corner/junction from actual neighbours.
        RoadLine(1, 3, 3, 3); RoadLine(1, 3, 1, 6); RoadLine(1, 6, 3, 6);
        RoadLine(3, 10, 5, 10); RoadLine(1, 16, 3, 16);
        RoadLine(3, 23, 6, 23); RoadLine(6, 23, 6, 34); RoadLine(3, 34, 6, 34);
        RoadLine(1, 53, 3, 53); RoadLine(1, 50, 1, 53);
        RoadLine(3, 57, 5, 57); RoadLine(5, 57, 5, 60);
        var roadTiles = new int[] { 0, 112, 101, 97, 100, 85, 73, 76, 113, 99, 74, 89, 75, 77, 88, 86 };
        var roads = new bool[Period, Width];
        for (int y = 0; y < Period; y++)
        for (int x = 0; x < Width; x++) roads[y, x] = Ports(layout[y, x]) != 0;
        for (int y = 0; y < Period; y++)
        for (int x = 0; x < Width; x++)
        {
            if (!roads[y, x]) continue;
            int mask = (roads[(y + 1) % Period, x] ? 1 : 0) | (x + 1 < Width && roads[y, x + 1] ? 2 : 0)
                | (roads[(y + Period - 1) % Period, x] ? 4 : 0) | (x == 0 || roads[y, x - 1] ? 8 : 0);
            layout[y, x] = mask == 5 && y % 2 == 0 ? 87 : mask == 10 && y % 2 == 0 ? 98 : roadTiles[mask];
        }
        // A larger island shows all four outside corners, with water-facing edges.
        for (int y = 42; y <= 46; y++)
        for (int x = 8; x <= 10; x++)
            layout[y, x] = y == 42 ? (x == 8 ? 61 : x == 10 ? 63 : 62)
                : y == 46 ? (x == 8 ? 37 : x == 10 ? 39 : 38) : x == 8 ? 49 : x == 10 ? 51 : 50;
    }

    void RoadLine(int x1, int y1, int x2, int y2)
    {
        for (int y = Mathf.Min(y1, y2); y <= Mathf.Max(y1, y2); y++)
        for (int x = Mathf.Min(x1, x2); x <= Mathf.Max(x1, x2); x++) layout[y, x] = 85;
    }

    void Coast(int bottom, int top)
    {
        for (int y = bottom; y <= top; y++)
        for (int x = 7; x < Width; x++)
        {
            // Grass outside, water inside/right; corners must match both edge tiles.
            layout[y, x] = x == 7 ? (y == bottom ? 52 : y == top ? 40 : 51)
                : y == bottom ? 38 : y == top ? 62 : 42;
        }
    }

    public void ResetScroll()
    {
        distance = 0;
        UpdateRows();
    }

    public void Scroll(float dt)
    {
        distance = (distance + dt * .65) % Period;
        UpdateRows();
    }

    void UpdateRows()
    {
        int whole = (int)System.Math.Floor(distance);
        float fraction = (float)(distance - whole);
        for (int r = 0; r < VisibleRows; r++)
        {
            int source = (whole + r) % Period;
            rows[r].localPosition = new Vector3(0, r - 10.5f - fraction, 0);
            if (sourceRows[r] == source + 1) continue;
            sourceRows[r] = source + 1;
            for (int c = 0; c < Width; c++) cells[r, c].sprite = art[layout[source, c]];
        }
    }

    static int Ports(int tile)
    {
        // north=1 east=2 south=4 west=8
        switch (tile) { case 85: case 87: return 5; case 74: case 98: return 10;
            case 76: return 7; case 77: return 13; case 113: return 8;
            case 73: return 6; case 75: return 12; case 86: return 15; case 88: return 14;
            case 89: return 11; case 97: return 3; case 99: return 9; case 100: return 4;
            case 101: return 2; case 112: return 1; default: return 0; }
    }

    public bool ValidateLayout()
    {
        for (int y = 0; y < Period; y++)
        for (int x = 0; x < Width; x++)
        {
            int ports = Ports(layout[y, x]);
            if (((ports & 1) != 0) != ((Ports(layout[(y + 1) % Period, x]) & 4) != 0)) return false;
            if (x + 1 < Width && (((ports & 2) != 0) != ((Ports(layout[y, x + 1]) & 8) != 0))) return false;
        }
        return layout[13, 9] == 64 && layout[12, 9] == 42 && layout[14, 9] == 42;
    }

    public bool ValidateCoverage()
    {
        if (transform.childCount != VisibleRows || rows[0].position.y > -10f || rows[VisibleRows - 1].position.y < 9f) return false;
        for (int r = 1; r < VisibleRows; r++)
            if (Mathf.Abs(rows[r].position.y - rows[r - 1].position.y - 1f) > .0001f) return false;
        return true;
    }
}
