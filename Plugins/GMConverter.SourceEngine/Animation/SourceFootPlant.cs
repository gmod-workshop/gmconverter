namespace GMConverter.SourceEngine.Animation;

// A stretch of a sequence where a foot stays on the ground, as an `ikrule footstep` range: the rule
// fades in from Start to Peak, holds to Tail and fades out by End, and holds the foot where it is
// on the Contact frame. On a loop the range may wrap past the end, with Start after Peak or Tail
// after End.
internal sealed record SourceFootPlant(int Start, int Peak, int Tail, int End, int Contact);
