using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using GameOffsets.Native;

namespace EzFollower;

public class PathFinder
{
    private readonly bool[][] _grid;

    private readonly int _dimension2;
    private readonly int _dimension1;

    public PathFinder(int[][] grid, int[] pathableValues)
    {
        var pv = pathableValues.ToHashSet();
        _grid = grid.Select(x => x.Select(y => pv.Contains(y)).ToArray()).ToArray();
        _dimension1 = _grid.Length;
        _dimension2 = _grid[0].Length;
    }

    private bool IsTilePathable(Vector2i tile)
    {
        if (tile.X < 0 || tile.X >= _dimension2)
        {
            return false;
        }

        if (tile.Y < 0 || tile.Y >= _dimension1)
        {
            return false;
        }

        return _grid[tile.Y][tile.X];
    }

    private static readonly List<Vector2i> NeighborOffsets = new List<Vector2i>
    {
        new Vector2i(0, 1),
        new Vector2i(1, 1),
        new Vector2i(1, 0),
        new Vector2i(1, -1),
        new Vector2i(0, -1),
        new Vector2i(-1, -1),
        new Vector2i(-1, 0),
        new Vector2i(-1, 1),
    };

    private static IEnumerable<Vector2i> GetNeighbors(Vector2i tile)
    {
        return NeighborOffsets.Select(offset => tile + offset);
    }

    private static float GetExactDistance(Vector2i tile, Dictionary<Vector2i, float> dict)
    {
        return dict.GetValueOrDefault(tile, float.PositiveInfinity);
    }

    public List<Vector2i> FindPath(Vector2i start, Vector2i target)
    {
        if (!IsTilePathable(start) || !IsTilePathable(target))
        {
            return null;
        }

        if (start == target)
        {
            return new List<Vector2i>();
        }

        // A* searches from the current start, so it remains valid when both endpoints move.
        var openTiles = new BinaryHeap<float, Vector2i>();
        var distanceFromStart = new Dictionary<Vector2i, float> { [start] = 0 };
        var previousTile = new Dictionary<Vector2i, Vector2i>();
        openTiles.Add(start.DistanceF(target), start);

        while (openTiles.TryRemoveTop(out var top))
        {
            var current = top.Value;
            if (current == target)
            {
                var path = new List<Vector2i>();
                while (current != start)
                {
                    path.Add(current);
                    current = previousTile[current];
                }

                path.Reverse();
                return path;
            }

            var currentDistance = distanceFromStart[current];
            foreach (var neighbor in GetNeighbors(current))
            {
                if (!IsTilePathable(neighbor))
                {
                    continue;
                }

                var proposedDistance = currentDistance + current.DistanceF(neighbor);
                if (proposedDistance >= GetExactDistance(neighbor, distanceFromStart))
                {
                    continue;
                }

                distanceFromStart[neighbor] = proposedDistance;
                previousTile[neighbor] = current;
                openTiles.Add(proposedDistance + neighbor.DistanceF(target), neighbor);
            }
        }

        return null;
    }
}