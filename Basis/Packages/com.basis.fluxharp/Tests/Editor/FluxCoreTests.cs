using System;
using System.Linq;
using NUnit.Framework;

namespace Basis.FluxHarp.Tests
{
    /// <summary>
    /// Requirement guardrails for the shared expression: the copies agree, lines mean what Desmos means,
    /// pitch is never snapped, the DJ switch cuts on a bar head, and the transcribed reference still sounds
    /// as written.
    /// </summary>
    public class FluxCoreTests
    {
        private static double Eval(FluxGraph g, ushort node, double x, int dj = 0)
        {
            FluxProgram p = FluxProgram.Compile(g);
            double[] reg = new double[FluxGraph.MaxNodes];
            p.EvaluateAll(x, dj, reg);
            Assert.That(p.TryGetRegister(node, out int r), Is.True);
            return reg[r];
        }

        private static double VoiceY(FluxGraph g, double x, int dj = 0, int index = 0)
        {
            FluxProgram p = FluxProgram.Compile(g);
            double[] reg = new double[FluxGraph.MaxNodes];
            FluxOutput v = p.Voices[index];
            p.Evaluate(v.Slice, x, dj, reg);
            return v.ReadMain(reg);
        }

        // ---- the shared session ------------------------------------------------------------------

        [Test]
        public void SequencedOpsConvergeAcrossCopies()
        {
            FluxGraph owner = new FluxGraph();
            FluxGraph peer = new FluxGraph();
            FluxOp[] requests =
            {
                FluxOp.Add(FluxKind.X, new FluxVec(0, 0, 0)),
                FluxOp.Add(FluxKind.Const, new FluxVec(0.1f, 0, 0), 2),
                FluxOp.Add(FluxKind.Mul, new FluxVec(0.2f, 0, 0)),
                FluxOp.Add(FluxKind.Voice, new FluxVec(0.3f, 0, 0)),
            };
            foreach (FluxOp request in requests)
            {
                FluxOp op = request;
                Assert.That(owner.Validate(ref op, 0), Is.Null);
                owner.Apply(op);
                peer.Apply(op);
            }
            foreach (FluxOp request in new[] { FluxOp.Connect(1, 3, 0), FluxOp.Connect(2, 3, 1), FluxOp.Connect(3, 4, 0), FluxOp.SetValue(2, 0.5) })
            {
                FluxOp op = request;
                Assert.That(owner.Validate(ref op, 0), Is.Null);
                owner.Apply(op);
                peer.Apply(op);
            }

            Assert.That(FluxCodec.Snapshot(9, peer), Is.EqualTo(FluxCodec.Snapshot(9, owner)));
            Assert.That(VoiceY(peer, 3), Is.EqualTo(1.5));
        }

        [Test]
        public void OwnerRejectsEditsThatWouldBreakTheExpression()
        {
            FluxGraph g = new FluxGraph();
            FluxBuilder b = new FluxBuilder(g);
            ushort sum = b.Add(b.X, 1).Node;
            ushort twice = b.Mul(FluxTerm.Of(sum), 2).Node;
            ushort line = b.Voice(FluxTerm.Of(twice));

            FluxOp cycle = FluxOp.Connect(twice, sum, 1);
            Assert.That(g.Validate(ref cycle, 0), Is.EqualTo("cycle"));

            FluxOp readLine = FluxOp.Connect(line, sum, 1);
            Assert.That(g.Validate(ref readLine, 0), Is.Not.Null);

            FluxOp far = FluxOp.Add(FluxKind.Const, new FluxVec(FluxGraph.BoardRadius + 0.1f, 0, 0));
            Assert.That(g.Validate(ref far, 0), Is.Not.Null);

            FluxOp notFinite = FluxOp.SetValue(b.Knob(1).Node, double.NaN);
            Assert.That(g.Validate(ref notFinite, 0), Is.Not.Null);

            FluxOp infiniteLiteral = FluxOp.SetLiteral(sum, 1, double.PositiveInfinity);
            Assert.That(g.Validate(ref infiniteLiteral, 0), Is.Not.Null);

            FluxOp tooFast = FluxOp.SetTempo(FluxGraph.MaxBpm + 1);
            Assert.That(g.Validate(ref tooFast, 0), Is.Not.Null);
        }

        [Test]
        public void SnapshotRoundTripsAndRejectsCorruption()
        {
            FluxGraph g = new FluxGraph();
            FluxPresets.Build(FluxPreset.Yxeni, g);
            g.Anchor = new FluxAnchor { Placed = true, Position = new FluxVec(1, 2, 3), Yaw = 45 };
            byte[] bytes = FluxCodec.Snapshot(77, g);

            Assert.That(FluxCodec.TryReadSnapshot(bytes, out uint seq, out FluxGraph copy), Is.True);
            Assert.That(seq, Is.EqualTo(77u));
            Assert.That(FluxCodec.Snapshot(77, copy), Is.EqualTo(bytes));

            Assert.That(FluxCodec.TryReadSnapshot(bytes.Take(bytes.Length - 3).ToArray(), out _, out _), Is.False);

            // Rewire a node to read itself: a snapshot that could not compile must not be accepted.
            FluxGraph bad = new FluxGraph();
            FluxBuilder b = new FluxBuilder(bad);
            ushort add = b.Add(b.X, 1).Node;
            bad.Get(add).Inputs[1] = add;
            Assert.That(FluxCodec.TryReadSnapshot(FluxCodec.Snapshot(1, bad), out _, out _), Is.False);
        }

        [Test]
        public void EveryPresetSurvivesTheWire()
        {
            foreach (FluxPreset preset in FluxPresets.All)
            {
                FluxGraph g = new FluxGraph();
                FluxPresets.Build(preset, g);
                Assert.That(g.Count, Is.LessThanOrEqualTo(FluxGraph.MaxNodes), preset.ToString());
                Assert.That(g.CountOutputs(FluxKind.Voice), Is.LessThanOrEqualTo(FluxGraph.MaxVoices), preset.ToString());
                Assert.That(g.Nodes.All(n => n.Position.Length <= FluxGraph.BoardRadius), Is.True, preset.ToString());
                Assert.That(FluxCodec.TryReadSnapshot(FluxCodec.Snapshot(1, g), out _, out _), Is.True, preset.ToString());
            }
        }

        // ---- meaning of a line ------------------------------------------------------------------

        [Test]
        public void UndefinedIsSilenceAndPropagates()
        {
            FluxGraph g = new FluxGraph();
            FluxBuilder b = new FluxBuilder(g);
            b.Voice(b.Add(b.If(b.Lt(b.X, 0), 1), 1));

            Assert.That(VoiceY(g, -1), Is.EqualTo(2));
            Assert.That(double.IsNaN(VoiceY(g, 1)), Is.True);

            FluxScore score = new FluxScore(FluxProgram.Compile(g), FluxTransport.StartingAt(0, 120));
            ushort line = score.Program.Voices[0].Node;
            FluxSound sound = new FluxSound();
            float[] buffer = new float[4800];
            // Beat 10 is x = 2: undefined, so after the release the line is silent.
            long ticks = (long)(10 / 2.0 * FluxTransport.TicksPerSecond);
            long sounding = (long)(7.8 / 2.0 * FluxTransport.TicksPerSecond); // x = −0.2
            sound.Render(score, line, sounding, 48000, buffer, buffer.Length);
            Assert.That(buffer.Max(Math.Abs), Is.GreaterThan(0.01f), "sounding where the branch is taken");
            sound.Render(score, line, ticks, 48000, buffer, buffer.Length);
            Assert.That(buffer.Skip(2400).Max(Math.Abs), Is.EqualTo(0));
        }

        [Test]
        public void PitchIsTheRawFloatNeverSnapped()
        {
            // A quarter-tone (1/24 octave) and a 7:4 harmonic seventh must come out as exactly those frequencies.
            Assert.That(FluxSpace.Frequency(1.0 / 24.0), Is.EqualTo(220 * Math.Pow(2, 1.0 / 24.0)).Within(1e-9));
            Assert.That(FluxSpace.Frequency(Math.Log(7.0 / 4.0, 2)), Is.EqualTo(385).Within(1e-9));

            FluxGraph g = new FluxGraph();
            FluxBuilder b = new FluxBuilder(g);
            b.Voice(Math.Log(7.0 / 4.0, 2));
            FluxScore score = new FluxScore(FluxProgram.Compile(g), FluxTransport.StartingAt(0, 120));
            float[] buffer = new float[48000];
            new FluxSound().Render(score, score.Program.Voices[0].Node, 0, 48000, buffer, buffer.Length);
            Assert.That(DominantHz(buffer, 48000), Is.EqualTo(385).Within(1.0));
        }

        [Test]
        public void ArithmeticFollowsDesmos()
        {
            FluxGraph g = new FluxGraph();
            FluxBuilder b = new FluxBuilder(g);
            ushort mod = b.Mod(-1, 7).Node;
            ushort frac = b.Frac(-0.25).Node;
            ushort div = b.Div(1, 0).Node;
            ushort clamp = b.Clamp(5, 0, 1).Node;
            ushort minOne = b.Min(b.Knob(3), FluxTerm.Unset).Node;

            Assert.That(Eval(g, mod, 0), Is.EqualTo(6));
            Assert.That(Eval(g, frac, 0), Is.EqualTo(0.75));
            Assert.That(double.IsNaN(Eval(g, div, 0)), Is.True);
            Assert.That(Eval(g, clamp, 0), Is.EqualTo(1));
            Assert.That(Eval(g, minOne, 0), Is.EqualTo(3));
        }

        [Test]
        public void PortLiteralIsTheNumberInPlaceAndTravelsWithEdits()
        {
            FluxGraph owner = new FluxGraph();
            FluxBuilder b = new FluxBuilder(owner);
            ushort scaled = b.Mul(12, b.Frac(b.X)).Node;
            Assert.That(Eval(owner, scaled, 0.5), Is.EqualTo(6));

            FluxGraph peer = owner.Clone();
            FluxOp op = FluxOp.SetLiteral(scaled, 0, 24);
            Assert.That(owner.Validate(ref op, 0), Is.Null);
            owner.Apply(op);
            FluxCodec.TryReadCommit(FluxCodec.Commit(1, op), out _, out FluxOp received);
            peer.Apply(received);
            Assert.That(Eval(peer, scaled, 0.5), Is.EqualTo(12));

            // NaN puts a port back to its own default: 1 for a multiplication.
            FluxOp reset = FluxOp.SetLiteral(scaled, 0, double.NaN);
            owner.Validate(ref reset, 0);
            owner.Apply(reset);
            Assert.That(Eval(owner, scaled, 0.5), Is.EqualTo(0.5));
            Assert.That(FluxCodec.TryReadSnapshot(FluxCodec.Snapshot(2, owner), out _, out FluxGraph copy), Is.True);
            Assert.That(Eval(copy, scaled, 0.5), Is.EqualTo(0.5));
        }

        // ---- the clock -------------------------------------------------------------------------

        [Test]
        public void DjSwitchCutsOnTheNextBarHead()
        {
            FluxGraph g = new FluxGraph { Transport = FluxTransport.StartingAt(0, 120) };
            long midBarTwo = (long)(5.5 / 2.0 * FluxTransport.TicksPerSecond); // beat 5.5
            FluxOp op = FluxOp.SetDj(2);
            Assert.That(g.Validate(ref op, midBarTwo), Is.Null);
            g.Apply(op);

            Assert.That(op.Beat, Is.EqualTo(8));
            Assert.That(g.Transport.DjAt(7.999), Is.EqualTo(0));
            Assert.That(g.Transport.DjAt(8), Is.EqualTo(2));

            // A second switch queued before the first lands keeps the first until its own bar.
            FluxOp again = FluxOp.SetDj(3);
            long beatNine = (long)(9.0 / 2.0 * FluxTransport.TicksPerSecond);
            g.Validate(ref again, beatNine);
            g.Apply(again);
            Assert.That(g.Transport.DjAt(10), Is.EqualTo(2));
            Assert.That(g.Transport.DjAt(12), Is.EqualTo(3));
        }

        [Test]
        public void TempoChangeKeepsTheBeatWhereItIs()
        {
            FluxTransport t = FluxTransport.StartingAt(1000, 120);
            long pivot = 1000 + 7 * TimeSpan.TicksPerSecond;
            FluxTransport faster = t.WithTempo(180, pivot);
            Assert.That(faster.BeatAt(pivot), Is.EqualTo(t.BeatAt(pivot)).Within(1e-6));
            Assert.That(faster.BeatAt(pivot + TimeSpan.TicksPerSecond) - faster.BeatAt(pivot), Is.EqualTo(3).Within(1e-6));
        }

        [Test]
        public void LoopRunsFromMinusEightToEight()
        {
            Assert.That(FluxTransport.XOf(0), Is.EqualTo(-8));
            Assert.That(FluxTransport.XOf(15.5), Is.EqualTo(7.5));
            Assert.That(FluxTransport.XOf(16), Is.EqualTo(-8));
            Assert.That(FluxTransport.XOf(-0.5), Is.EqualTo(7.5));
        }

        // ---- space ------------------------------------------------------------------------------

        [Test]
        public void UnwiredVoiceRidesThePlayheadRing()
        {
            FluxVec start = FluxSpace.VoicePosition(0, double.NaN, double.NaN, -8);
            FluxVec quarter = FluxSpace.VoicePosition(0, double.NaN, double.NaN, -4);
            FluxVec octaveUp = FluxSpace.VoicePosition(1, double.NaN, double.NaN, -8);

            Assert.That(start.Z, Is.EqualTo((float)FluxSpace.RingRadius).Within(1e-5)); // ahead at loop start
            Assert.That(quarter.X, Is.EqualTo((float)FluxSpace.RingRadius).Within(1e-5)); // to the right a quarter in
            Assert.That(octaveUp.Y - start.Y, Is.EqualTo((float)FluxSpace.HeightPerOctave).Within(1e-5));

            FluxVec wired = FluxSpace.VoicePosition(0, 0.5, 1, 0);
            Assert.That(wired.Z, Is.EqualTo(-1).Within(1e-5)); // θ = ½ turn puts it behind
        }

        // ---- the reference ----------------------------------------------------------------------

        [Test]
        public void FourthsTranscriptionMatchesTheReferenceFormula()
        {
            FluxGraph g = new FluxGraph();
            FluxPresets.Build(FluxPreset.Fourths, g);
            for (double x = -8; x < 8; x += 0.03125 * 0.9)
            {
                double expected = Reference(x);
                double actual = VoiceY(g, x);
                if (double.IsNaN(expected)) Assert.That(double.IsNaN(actual), Is.True, $"x={x}");
                else Assert.That(actual, Is.EqualTo(expected).Within(1e-9), $"x={x}");
            }

            static double Mod(double a, double n) => a - n * Math.Floor(a / n);
            static double Frac(double a) => a - Math.Floor(a);
            static double Reference(double x)
            {
                if (Frac(x) < 0.25)
                {
                    return -2 + Math.Floor((12 * Mod(3 * Math.Floor(x / 2) + 5, 7) + 2) / 7) / 12 + 2.5 * Math.Exp(-22 * Frac(x));
                }
                if (Frac(4 * x) < 0.8)
                {
                    double q = Math.Floor(x / 4);
                    double inner = 3 * Math.Floor(x / 2) + 2 * (Mod(Math.Floor(4 * x), 4) + Mod(Math.Floor(x), 2)) + 1 - 2 * q;
                    double degree = Mod(inner, 7) + 2 * q + 4;
                    double accent = (Math.Floor(x / 2) == 2 ? 1 : 0) * (Mod(Math.Floor(4 * x), 8) == 1 ? 1 : 0);
                    return Math.Floor((12 * degree + 2) / 7) / 12 + accent / 12 + Math.Exp(-12 * Frac(4 * x)) / 6;
                }
                return double.NaN;
            }
        }

        [Test]
        public void DemoPieceTranslatesTheGuestNoteByDjMode()
        {
            FluxGraph g = new FluxGraph();
            FluxPresets.Build(FluxPreset.Yxeni, g);
            FluxProgram p = FluxProgram.Compile(g);
            double[] reg = new double[FluxGraph.MaxNodes];
            FluxOutput guestPad = p.Voices[3];

            double[] expectedRatios = { 11.0 / 6.0, 7.0 / 4.0, 5.0 / 2.0, 11.0 / 4.0 };
            for (int dj = 0; dj < FluxGraph.DjModes; dj++)
            {
                p.Evaluate(guestPad.Slice, -7.5, dj, reg);
                double y = guestPad.ReadMain(reg);
                Assert.That(Math.Pow(2, y + 1), Is.EqualTo(expectedRatios[dj]).Within(1e-9), $"dj {dj}");
            }
        }

        private static double DominantHz(float[] samples, int rate)
        {
            // Zero crossings over the second half, after the attack.
            int crossings = 0;
            int start = samples.Length / 2;
            for (int i = start + 1; i < samples.Length; i++)
            {
                if (samples[i - 1] < 0 && samples[i] >= 0) crossings++;
            }
            return crossings / ((samples.Length - start) / (double)rate);
        }
    }
}
