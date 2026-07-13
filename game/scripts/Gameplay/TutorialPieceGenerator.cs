using System.Collections.Generic;
using Blockfall.Core;

namespace Blockfall.Gameplay;

/// <summary>
/// A 7-bag generator the tutorial can steer: <see cref="Force"/> injects specific
/// pieces to the front of the queue (used by the T-spin lesson to guarantee a T),
/// while everything else falls through to a normal fair bag.
/// </summary>
public sealed class TutorialPieceGenerator : IPieceGenerator
{
    private readonly SevenBagGenerator _bag;
    private readonly Queue<PieceType> _forced = new();

    public TutorialPieceGenerator(IRandomSource rng) => _bag = new SevenBagGenerator(rng);

    /// <summary>Queue a piece to be dealt before any bag pieces.</summary>
    public void Force(PieceType type) => _forced.Enqueue(type);

    public PieceType Next() => _forced.Count > 0 ? _forced.Dequeue() : _bag.Next();

    public IReadOnlyList<PieceType> Preview(int count)
    {
        var list = new List<PieceType>(count);
        foreach (var f in _forced)
        {
            if (list.Count >= count) break;
            list.Add(f);
        }
        if (list.Count < count)
            list.AddRange(_bag.Preview(count - list.Count));
        return list;
    }
}
