using System;
using System.Collections.Generic;
using Basis.Scripts.Drivers;
using TMPro;
using UnityEngine;

namespace Basis.FluxHarp
{
    /// <summary>
    /// The instrument in the world. Three kinds of thing, and nothing else:
    /// <list type="bullet">
    /// <item><b>Voice dots</b> (声ドット) — the glowing points that sound. One per line, flying where the line
    /// puts it, trailing the path it is about to take.</item>
    /// <item><b>The node sphere</b> (ノード球) — the editing board, a sphere round the player standing at the
    /// anchor. Every node of the shared expression is a card on a reading cylinder at arm's length, ProtoFlux
    /// style: name on top, inputs down the left with their numbers, output on the right with its live value.
    /// Anyone can grab, wire, dial, drop.</item>
    /// <item><b>The DJ cubes</b> (DJキューブ) — four, for the four modes, at the waist behind the player, under
    /// the palette of node names. Touching one switches the
    /// whole piece at the next bar head, as a hard cut.</item>
    /// </list>
    /// All of it is white light in the dark.
    /// </summary>
    public sealed class FluxHarpInstrument : MonoBehaviour, IFluxGripTarget
    {
        // Roles of a grip: what the collider it sits on stands for.
        private const int RoleNode = 0, RolePort = 1, RoleOutPort = 2, RolePalette = 3, RoleDj = 4, RoleValue = 5, RoleLiteral = 6;

        /// <summary>Where an unwired port's own number sits in its row, from the card's left edge, and how wide it is.</summary>
        private const float LiteralInset = 0.085f;
        private const float LiteralWidth = 0.05f;

        /// <summary>Height of the node sphere's centre above the anchor: about the chest of a standing player.</summary>
        private const float BoardHeight = 1.3f;

        private const float PortRadius = 0.016f;
        private const float DjSize = 0.1f;
        private const float DjRadius = 0.45f;
        private const float DjDrop = 0.5f;
        private const float VoidRadius = 30f;
        private const float TextSize = 0.017f;
        private const float HeaderTextSize = 0.021f;

        private static readonly Color White = new Color(1f, 1f, 1f, 1f);

        public static FluxHarpInstrument Instance { get; private set; }

        /// <summary>Where the viewer's eye is. The local camera, unless something else is looking (a capture).</summary>
        public Transform EyeOverride;

        /// <summary>Whether lines get a sound source. Off for an offline render, which only draws.</summary>
        public bool Audible = true;

        private FluxSession _session;
        private Func<long> _now;
        private FluxGlow _glow;
        private Material _glowMaterial;
        private Transform _glowObject;
        private Transform _board;
        private Transform _lines;
        private TMP_FontAsset _font;

        /// <summary>What this instrument's lines play from. Its own, so a second instrument (a capture, a test) never steals the score.</summary>
        private readonly FluxHarpEmitter.Stage _stage = new FluxHarpEmitter.Stage();
        private FluxProgram _program = FluxProgram.Empty;
        private readonly double[] _registers = new double[FluxGraph.MaxNodes];
        private readonly double[] _scratch = new double[FluxGraph.MaxNodes];
        private readonly Dictionary<ushort, FluxHarpEmitter> _emitters = new Dictionary<ushort, FluxHarpEmitter>();
        private readonly Dictionary<ushort, Card> _cards = new Dictionary<ushort, Card>();
        private readonly Dictionary<ushort, Vector3> _remoteDrag = new Dictionary<ushort, Vector3>();
        private readonly Dictionary<ushort, Vector3> _linePositions = new Dictionary<ushort, Vector3>();
        private readonly List<Transform> _palette = new List<Transform>();
        private readonly List<Vector3> _pathPoints = new List<Vector3>(256);
        private readonly List<Color> _pathColors = new List<Color>(256);
        private bool _structureDirty = true;
        private float _flashUntil;

        // What the local hand is doing.
        private FluxGrip _held;
        private float _heldDistance;
        private Vector3 _heldPoint;
        private Vector3 _pressPoint;
        private Vector3 _grabOffset;
        private Vector3 _pressRight;
        private Vector3 _pressUp;
        private double _pressValue;
        private FluxGrip _hovered;

        /// <summary>A node's card. Its transform sits at the card centre, turned to face the player's axis.</summary>
        private sealed class Card
        {
            public FluxKind Kind;
            public Transform Root;
            public TextMeshPro Name;
            public TextMeshPro Live;
            public TextMeshPro[] Rows;

            /// <summary>Each port's own number, and the grip on it; shown and held only while the port is unwired.</summary>
            public TextMeshPro[] Literals;
            public Collider[] LiteralGrips;
            public string[] Shown;
        }

        // ---- lifecycle --------------------------------------------------------------------------

        public static FluxHarpInstrument Spawn()
        {
            if (Instance != null) return Instance;
            GameObject go = new GameObject("Flux Harp");
            DontDestroyOnLoad(go);
            FluxHarpNetwork.Initialize();
            FluxHarpInstrument instrument = go.AddComponent<FluxHarpInstrument>();
            instrument.Setup(FluxHarpNetwork.Session, () => FluxHarpNetwork.NowTicks);
            Instance = instrument;
            return instrument;
        }

        private float _placeAt = -1;
        private Vector3 _lastFeet;
        private int _stillFrames;

        /// <summary>
        /// Arriving somewhere (the first spawn, a respawn, a teleport) puts the instrument round you once you have
        /// come to rest, when the piece is yours to place: alone, or as the room's sequencer. Someone else's room
        /// keeps the anchor everyone already shares.
        /// </summary>
        public void PlaceOnArrival()
        {
            _placeAt = Time.unscaledTime;
            _stillFrames = 0;
        }

        private void PlaceWhenSettled()
        {
            if (_placeAt < 0) return;
            Transform eye = EyeTransform();
            if (eye == null) return;
            Vector3 feet = eye.position;
            _stillFrames = (feet - _lastFeet).sqrMagnitude < 1e-6f ? _stillFrames + 1 : 0;
            _lastFeet = feet;
            // Still for half a second, or given up waiting after ten: a falling or snapping body has landed.
            if (_stillFrames < 30 && Time.unscaledTime - _placeAt < 10) return;
            _placeAt = -1;
            if (_session.IsOwner) PlaceHere();
        }

        public static void Despawn()
        {
            if (Instance != null) Destroy(Instance.gameObject);
        }

        /// <summary>
        /// Builds the instrument around a session. The client binds the room's session; an offline render binds
        /// one of its own, so what it draws is exactly what a player sees.
        /// </summary>
        public void Setup(FluxSession session, Func<long> now)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _now = now ?? throw new ArgumentNullException(nameof(now));
            Shader glowShader = Resources.Load<Shader>("FluxHarpGlow") ?? throw new InvalidOperationException("FluxHarpGlow shader missing.");
            Shader voidShader = Resources.Load<Shader>("FluxHarpVoid") ?? throw new InvalidOperationException("FluxHarpVoid shader missing.");
            _font = TMP_Settings.defaultFontAsset ?? throw new InvalidOperationException("No default TextMeshPro font.");
            _glowMaterial = new Material(glowShader) { name = "Flux Glow", enableInstancing = true };
            _glowMaterial.SetFloat("_Intensity", 3.2f);
            _glow = new FluxGlow(_glowMaterial);

            GameObject glowObject = new GameObject("Flux Light");
            glowObject.transform.SetParent(transform, false);
            _glowObject = glowObject.transform;
            glowObject.AddComponent<MeshFilter>().sharedMesh = _glow.Mesh;
            MeshRenderer glowRenderer = glowObject.AddComponent<MeshRenderer>();
            glowRenderer.sharedMaterial = _glowMaterial;
            glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            glowRenderer.receiveShadows = false;

            GameObject voidSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            voidSphere.name = "Flux Void";
            DestroyNow(voidSphere.GetComponent<Collider>());
            voidSphere.transform.SetParent(transform, false);
            voidSphere.transform.localScale = Vector3.one * VoidRadius * 2;
            MeshRenderer voidRenderer = voidSphere.GetComponent<MeshRenderer>();
            voidRenderer.sharedMaterial = new Material(voidShader) { name = "Flux Void" };
            voidRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _board = new GameObject("Node Sphere").transform;
            _board.SetParent(transform, false);
            _board.localPosition = new Vector3(0, BoardHeight, 0);
            _lines = new GameObject("Lines").transform;
            _lines.SetParent(transform, false);

            BuildPalette();
            BuildDjCubes();

            _session.Changed += OnSessionChanged;
            _session.RemoteDrag += OnRemoteDrag;
            _session.Rejected += OnRejected;
            OnSessionChanged(null);
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                _session.Changed -= OnSessionChanged;
                _session.RemoteDrag -= OnRemoteDrag;
                _session.Rejected -= OnRejected;
            }
            if (Instance == this) Instance = null;
        }

        private static void DestroyNow(UnityEngine.Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        private void Request(FluxOp op) => _session.Request(op, _now());

        /// <summary>What the instrument is doing, in words: the piece, and each sounding line's place and level.</summary>
        public string Report()
        {
            System.Text.StringBuilder s = new System.Text.StringBuilder();
            FluxGraph graph = _session.Graph;
            s.AppendLine($"owner={_session.IsOwner} nodes={graph.Count} voices={_program.Voices.Length} drums={_program.Drums.Length} bpm={graph.Transport.Bpm} dj={graph.Transport.Dj}");
            Transform eye = EyeTransform();
            s.AppendLine($"eye={(eye != null ? eye.position.ToString() : "none")} forward={(eye != null ? eye.forward.ToString() : "none")} eyeHeight={BasisHeightDriver.ScaledAvatarEyeHeight:F2} glowVerts={_glow.VertexCount} cards={_cards.Count}");
            s.AppendLine($"anchor={transform.position} spatializer={AudioSettings.GetSpatializerPluginName()} rate={AudioSettings.outputSampleRate} gain={FluxHarpSettings.Gain:F3}");
            foreach (KeyValuePair<ushort, FluxHarpEmitter> pair in _emitters)
            {
                AudioSource source = pair.Value.GetComponent<AudioSource>();
                s.AppendLine($"line {pair.Key}: at={pair.Value.transform.position} peak={pair.Value.Peak:F4} playing={source.isPlaying} spatialize={source.spatialize} post={source.spatializePostEffects}");
                pair.Value.Peak = 0;
            }
            return s.ToString();
        }

        /// <summary>Moves the instrument to stand around the local player, for everyone: centred on their feet, facing where they look.</summary>
        public void PlaceHere()
        {
            Transform eye = EyeTransform();
            if (eye == null) return;
            // What the player sees from is the eye, in every mode (desktop, VR, a capture): the instrument stands
            // an eye height under it, so the node sphere sits at their chest.
            Vector3 feet = eye.position - Vector3.up * Mathf.Max(0.5f, BasisHeightDriver.ScaledAvatarEyeHeight);
            Vector3 forward = Vector3.ProjectOnPlane(eye.forward, Vector3.up);
            float yaw = forward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(forward).eulerAngles.y : 0;
            Request(FluxOp.SetAnchor(new FluxVec(feet.x, feet.y, feet.z), yaw));
        }

        private Transform EyeTransform()
        {
            if (EyeOverride != null) return EyeOverride;
            if (BasisLocalCameraDriver.CameraInstance != null) return BasisLocalCameraDriver.CameraInstance.transform;
            return Camera.main != null ? Camera.main.transform : null;
        }

        // ---- the shared expression --------------------------------------------------------------

        private void OnSessionChanged(FluxOp? op)
        {
            if (op == null || op.Value.Type != FluxOpType.MoveNode && op.Value.Type != FluxOpType.SetAnchor) _structureDirty = true;
            if (op.HasValue && op.Value.Type == FluxOpType.MoveNode) _remoteDrag.Remove(op.Value.Node);

            FluxAnchor anchor = _session.Graph.Anchor;
            if (anchor.Placed)
            {
                transform.SetPositionAndRotation(ToVector(anchor.Position), Quaternion.Euler(0, anchor.Yaw, 0));
            }
        }

        private void OnRemoteDrag(ushort node, FluxVec position) => _remoteDrag[node] = ToVector(position);

        private void OnRejected(string why)
        {
            Debug.Log($"[FluxHarp] edit rejected: {why}");
            _flashUntil = Time.time + 1.5f;
        }

        private void Rebuild()
        {
            _structureDirty = false;
            FluxGraph graph = _session.Graph;
            _program = FluxProgram.Compile(graph);

            HashSet<ushort> alive = new HashSet<ushort>();
            foreach (FluxNode node in graph.Nodes)
            {
                alive.Add(node.Id);
                if (_cards.TryGetValue(node.Id, out Card card) && card.Kind == node.Kind) continue;
                if (card != null) DestroyNow(card.Root.gameObject);
                _cards[node.Id] = CreateCard(node);
            }
            List<ushort> gone = new List<ushort>();
            foreach (ushort id in _cards.Keys) if (!alive.Contains(id)) gone.Add(id);
            foreach (ushort id in gone)
            {
                DestroyNow(_cards[id].Root.gameObject);
                _cards.Remove(id);
            }

            // Emitters: one per line that sounds.
            HashSet<ushort> lines = new HashSet<ushort>();
            foreach (FluxOutput v in _program.Voices) lines.Add(v.Node);
            foreach (FluxOutput d in _program.Drums) lines.Add(d.Node);
            if (Audible)
            {
                foreach (ushort line in lines)
                {
                    if (!_emitters.ContainsKey(line)) _emitters[line] = FluxHarpEmitter.Create(_lines, _stage, line);
                }
            }
            gone.Clear();
            foreach (ushort line in _emitters.Keys) if (!lines.Contains(line)) gone.Add(line);
            foreach (ushort line in gone)
            {
                DestroyNow(_emitters[line].gameObject);
                _emitters.Remove(line);
            }

            if (_held != null && _held.Role != RolePalette && _held.Role != RoleDj && !_cards.ContainsKey(_held.Node)) _held = null;
        }

        private Card CreateCard(FluxNode node)
        {
            FluxKindInfo info = node.Info;
            GameObject root = new GameObject($"Node {node.Id} {info.Label}");
            root.transform.SetParent(_board, false);
            float height = FluxCard.Height(node.Kind);
            root.AddComponent<BoxCollider>().size = new Vector3(FluxCard.Width, height, 0.02f);
            FluxGrip.Add(root, this, node.Kind == FluxKind.Const || node.Kind == FluxKind.Drum ? RoleValue : RoleNode, node.Id);

            Card card = new Card
            {
                Kind = node.Kind,
                Root = root.transform,
                Rows = new TextMeshPro[info.Arity],
                Literals = new TextMeshPro[info.Arity],
                LiteralGrips = new Collider[info.Arity],
                Shown = new string[2 + info.Arity],
            };
            float top = height * 0.5f;
            float headerY = top - FluxCard.Header * 0.5f;
            card.Name = Text(root.transform, info.Label, HeaderTextSize, new Vector3(-FluxCard.Width * 0.5f + FluxCard.Pad, headerY, 0), TextAlignmentOptions.Left);
            for (int p = 0; p < info.Arity; p++)
            {
                FluxVec at = FluxCard.Input(node.Kind, p);
                GameObject port = new GameObject($"in {info.Ports[p].Name}");
                port.transform.SetParent(root.transform, false);
                port.transform.localPosition = ToVector(at);
                port.AddComponent<SphereCollider>().radius = PortRadius;
                FluxGrip.Add(port, this, RolePort, node.Id, p);
                card.Rows[p] = Text(root.transform, info.Ports[p].Name, TextSize, new Vector3(at.X + PortRadius + 0.004f, at.Y, 0), TextAlignmentOptions.Left);

                // The port's own number: dial it sideways, pull it down to reset, pull it off the card to make it a node.
                Vector3 literalAt = new Vector3(at.X + LiteralInset, at.Y, 0);
                card.Literals[p] = Text(root.transform, "", TextSize, literalAt - new Vector3(LiteralWidth * 0.5f, 0, 0), TextAlignmentOptions.Left);
                GameObject literal = new GameObject($"number {info.Ports[p].Name}");
                literal.transform.SetParent(root.transform, false);
                literal.transform.localPosition = literalAt;
                BoxCollider literalBox = literal.AddComponent<BoxCollider>();
                literalBox.size = new Vector3(LiteralWidth, FluxCard.Row * 0.9f, 0.025f);
                FluxGrip.Add(literal, this, RoleLiteral, node.Id, p);
                card.LiteralGrips[p] = literalBox;
            }
            if (info.IsOutput)
            {
                // A line's live value (its pitch, or its drum envelope) sits in the header, opposite its name.
                card.Live = Text(root.transform, "", TextSize, new Vector3(FluxCard.Width * 0.5f - FluxCard.Pad, headerY, 0), TextAlignmentOptions.Right);
            }
            else
            {
                FluxVec at = FluxCard.Output(node.Kind);
                GameObject output = new GameObject("out");
                output.transform.SetParent(root.transform, false);
                output.transform.localPosition = ToVector(at);
                output.AddComponent<SphereCollider>().radius = PortRadius;
                FluxGrip.Add(output, this, RoleOutPort, node.Id);
                card.Live = Text(root.transform, "", TextSize, new Vector3(at.X - PortRadius - 0.004f, at.Y, 0), TextAlignmentOptions.Right);
            }
            return card;
        }

        private TextMeshPro Text(Transform parent, string text, float size, Vector3 at, TextAlignmentOptions alignment)
        {
            GameObject go = new GameObject("text");
            go.transform.SetParent(parent, false);
            TextMeshPro tmp = go.AddComponent<TextMeshPro>();
            tmp.font = _font;
            tmp.fontSize = size * 10f;
            tmp.color = White;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.alignment = alignment;
            RectTransform rect = tmp.rectTransform;
            bool left = alignment == TextAlignmentOptions.Left;
            bool right = alignment == TextAlignmentOptions.Right;
            rect.pivot = new Vector2(left ? 0 : right ? 1 : 0.5f, 0.5f);
            rect.sizeDelta = new Vector2(FluxCard.Width * 0.8f, FluxCard.Row);
            rect.localPosition = at;
            tmp.text = text;
            return tmp;
        }

        private static void SetText(Card card, int slot, TextMeshPro tmp, string text)
        {
            if (card.Shown[slot] == text) return;
            card.Shown[slot] = text;
            tmp.text = text;
        }

        // ---- the palette and the DJ cubes ------------------------------------------------------

        /// <summary>
        /// Every node kind, as a grid of names on the reading cylinder behind the player, where the expression's
        /// sheet never wraps to. Pull a name into the sphere to make that node there.
        /// </summary>
        private void BuildPalette()
        {
            FluxKind[] kinds = FluxKindInfo.Palette;
            const int Columns = 7;
            int rows = (kinds.Length + Columns - 1) / Columns;
            float colStep = FluxCard.Width * 0.9f;
            float rowStep = FluxCard.Header + FluxCard.Gap;
            for (int i = 0; i < kinds.Length; i++)
            {
                int col = i % Columns, row = i / Columns;
                float arc = (col - (Columns - 1) * 0.5f) * colStep;
                float y = ((rows - 1) * 0.5f - row) * rowStep - 0.15f;
                // Behind the player: the arc is measured round from straight back.
                FluxVec front = FluxCard.OnCylinder(arc, y);
                Vector3 at = new Vector3(-front.X, front.Y, -front.Z);
                GameObject go = new GameObject($"Palette {kinds[i]}");
                go.transform.SetParent(_board, false);
                go.transform.localPosition = at;
                go.transform.localRotation = FacingAxis(at);
                go.AddComponent<BoxCollider>().size = new Vector3(colStep * 0.9f, FluxCard.Header, 0.02f);
                FluxGrip.Add(go, this, RolePalette, 0, i);
                Text(go.transform, FluxKindInfo.Of(kinds[i]).Label, HeaderTextSize, Vector3.zero, TextAlignmentOptions.Center);
                _palette.Add(go.transform);
            }
        }

        private void BuildDjCubes()
        {
            for (int m = 0; m < FluxGraph.DjModes; m++)
            {
                GameObject go = new GameObject($"DJ Cube {m}");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = DjCubePosition(m);
                go.AddComponent<BoxCollider>().size = Vector3.one * DjSize;
                FluxGrip.Add(go, this, RoleDj, 0, m);
            }
        }

        /// <summary>
        /// The four cubes on a short arc at the waist behind the player, under the palette: turn round to make,
        /// turn round to switch; the front belongs to the expression.
        /// </summary>
        private static Vector3 DjCubePosition(int mode)
        {
            float a = (mode - (FluxGraph.DjModes - 1) * 0.5f) * 0.3f;
            return new Vector3(-Mathf.Sin(a) * DjRadius, BoardHeight - DjDrop, -Mathf.Cos(a) * DjRadius);
        }

        /// <summary>Turns a card at board-local <paramref name="at"/> so the player on the axis reads it.</summary>
        private static Quaternion FacingAxis(Vector3 at)
        {
            Vector3 outward = new Vector3(at.x, 0, at.z);
            return outward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(outward, Vector3.up) : Quaternion.identity;
        }

        // ---- grips ------------------------------------------------------------------------------

        public void Hover(FluxGrip grip, bool hovered)
        {
            if (hovered) _hovered = grip;
            else if (_hovered == grip) _hovered = null;
        }

        public void Press(FluxGrip grip, in FluxPointer pointer)
        {
            if (grip.Role == RoleDj)
            {
                Request(FluxOp.SetDj(grip.Index));
                return;
            }
            _held = grip;
            _heldDistance = Vector3.Distance(pointer.Origin, grip.transform.position);
            _heldPoint = pointer.At(_heldDistance);
            _pressPoint = _heldPoint;
            _pressRight = grip.transform.right;
            _pressUp = grip.transform.up;
            _grabOffset = grip.transform.position - _heldPoint;
            _pressValue = 0;
            if (!_session.Graph.TryGet(grip.Node, out FluxNode node)) return;
            if (grip.Role == RoleValue) _pressValue = node.Value;
            if (grip.Role == RoleLiteral && IsFinite(node.Literals[grip.Index])) _pressValue = node.Literals[grip.Index];
        }

        public void Hold(FluxGrip grip, in FluxPointer pointer)
        {
            if (_held != grip) return;
            _heldPoint = pointer.At(_heldDistance);
            if (Moving())
            {
                FluxVec local = BoardLocal(_heldPoint + _grabOffset);
                if (local.Length <= FluxGraph.BoardRadius) _session.Drag(grip.Node, local);
            }
        }

        /// <summary>The held card is being carried (not dialled).</summary>
        private bool Moving() => _held != null && (_held.Role == RoleNode || _held.Role == RoleValue && !Sideways());

        private Vector2 PullSincePress()
        {
            Vector3 d = _heldPoint - _pressPoint;
            return new Vector2(Vector3.Dot(d, _pressRight), Vector3.Dot(d, _pressUp));
        }

        /// <summary>Moved mostly along the card: a dial, not a carry.</summary>
        private bool Sideways()
        {
            Vector2 d = PullSincePress();
            float depth = (_heldPoint - _pressPoint - _pressRight * d.x - _pressUp * d.y).magnitude;
            return Mathf.Abs(d.x) > 0.02f && Mathf.Abs(d.x) > 1.5f * Mathf.Max(Mathf.Abs(d.y), depth);
        }

        /// <summary>Pulled mostly straight down off an unwired input: back to its default.</summary>
        private bool PulledDown()
        {
            Vector2 d = PullSincePress();
            return d.y < -0.08f && -d.y > 1.5f * Mathf.Abs(d.x);
        }

        /// <summary>Moved far enough from where it was taken, in no particular direction, to mean "away".</summary>
        private bool Pulled() => Vector3.Distance(_heldPoint, _pressPoint) > 0.06f;

        private double Dialled()
        {
            float dx = PullSincePress().x;
            if (_held.Role == RoleValue && _session.Graph.TryGet(_held.Node, out FluxNode node) && node.Kind == FluxKind.Drum)
            {
                return Mathf.Clamp((int)Math.Round(_pressValue + dx / 0.08f), 0, (int)FluxDrumType.Hat);
            }
            // Fine near the start, coarse further out: a centimetre is a hundredth, half a metre is about ten.
            double delta = Math.Sign(dx) * (Math.Pow(Math.Abs(dx) * 10, 2.2) * 0.5 + Math.Abs(dx) * 0.4);
            return Math.Round((_pressValue + delta) * 1000) / 1000;
        }

        public void Release(FluxGrip grip, in FluxPointer pointer)
        {
            if (_held != grip) return;
            _heldPoint = pointer.At(_heldDistance);
            bool sideways = Sideways();
            bool pulledDown = PulledDown();
            double dialled = Dialled();
            _held = null;

            FluxVec local = BoardLocal(_heldPoint + _grabOffset);
            bool inside = local.Length <= FluxGraph.BoardRadius;
            FluxGraph graph = _session.Graph;

            switch (grip.Role)
            {
                case RolePalette:
                    FluxKind kind = FluxKindInfo.Palette[grip.Index];
                    if (inside && Vector3.Distance(_heldPoint, _pressPoint) > 0.05f) Request(FluxOp.Add(kind, local, kind == FluxKind.Const ? 1 : 0));
                    break;

                case RoleValue when sideways:
                    Request(FluxOp.SetValue(grip.Node, dialled));
                    break;

                case RoleValue when graph.TryGet(grip.Node, out FluxNode number) && number.Kind == FluxKind.Const
                                    && TryPortAt(_heldPoint, out ushort intoNode, out int intoPort) && intoNode != grip.Node:
                    // A number node let go on a port becomes that port's own number.
                    Request(FluxOp.Inline(grip.Node, intoNode, intoPort));
                    break;

                case RoleNode:
                case RoleValue:
                    // Thrown out of the sphere: gone.
                    Request(inside ? FluxOp.Move(grip.Node, local) : FluxOp.Remove(grip.Node));
                    break;

                case RoleLiteral:
                    // A port's own number: dialled sideways, pulled down back to the port's default, or pulled
                    // off the card into the sphere, where it becomes a number node wired into the port.
                    if (sideways) Request(FluxOp.SetLiteral(grip.Node, grip.Index, dialled));
                    else if (pulledDown) Request(FluxOp.SetLiteral(grip.Node, grip.Index, double.NaN));
                    else if (Pulled() && inside) Request(FluxOp.Extract(grip.Node, grip.Index, BoardLocal(_heldPoint)));
                    break;

                case RoleOutPort:
                    if (TryPortAt(_heldPoint, out ushort dstNode, out int dstPort)) Request(FluxOp.Connect(grip.Node, dstNode, dstPort));
                    break;

                case RolePort:
                    if (!graph.TryGet(grip.Node, out FluxNode node)) break;
                    // Pulled off an input onto an output: wire it. A wired input pulled away is cut, and reads
                    // its own number again.
                    if (TryOutAt(_heldPoint, out ushort src)) Request(FluxOp.Connect(src, grip.Node, grip.Index));
                    else if (node.Inputs[grip.Index] != FluxGraph.None && Pulled()) Request(FluxOp.Disconnect(grip.Node, grip.Index));
                    break;
            }
        }

        private bool TryPortAt(Vector3 world, out ushort node, out int port)
        {
            node = 0;
            port = -1;
            float best = 0.05f;
            foreach (KeyValuePair<ushort, Card> pair in _cards)
            {
                for (int p = 0; p < pair.Value.Rows.Length; p++)
                {
                    float d = Vector3.Distance(world, InputWorld(pair.Value, p));
                    if (d >= best) continue;
                    best = d;
                    node = pair.Key;
                    port = p;
                }
            }
            return port >= 0;
        }

        private bool TryOutAt(Vector3 world, out ushort node)
        {
            node = 0;
            float best = 0.05f;
            foreach (KeyValuePair<ushort, Card> pair in _cards)
            {
                if (FluxKindInfo.Of(pair.Value.Kind).IsOutput) continue;
                float d = Vector3.Distance(world, OutputWorld(pair.Value));
                if (d >= best) continue;
                best = d;
                node = pair.Key;
            }
            return node != FluxGraph.None;
        }

        private static Vector3 InputWorld(Card card, int port) => card.Root.TransformPoint(ToVector(FluxCard.Input(card.Kind, port)));

        private static Vector3 OutputWorld(Card card) => card.Root.TransformPoint(ToVector(FluxCard.Output(card.Kind)));

        private FluxVec BoardLocal(Vector3 world)
        {
            Vector3 l = _board.InverseTransformPoint(world);
            return new FluxVec(l.x, l.y, l.z);
        }

        private static Vector3 ToVector(FluxVec v) => new Vector3(v.X, v.Y, v.Z);

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        // ---- every frame ------------------------------------------------------------------------

        private void Update()
        {
            PlaceWhenSettled();
            Frame();
        }

        /// <summary>One frame of the instrument: clock, sound positions, cards, light.</summary>
        public void Frame()
        {
            if (_session == null) return;
            if (_structureDirty) Rebuild();

            long now = _now();
            _stage.Clock.Observe(AudioSettings.dspTime, now);
            FluxGraph graph = _session.Graph;
            FluxTransport transport = graph.Transport;
            _stage.Score = new FluxScore(_program, transport);

            double beat = transport.BeatAt(now);
            double x = FluxTransport.XOf(beat);
            int dj = transport.DjAt(beat);
            _program.EvaluateAll(x, dj, _registers);

            // Every line's place now. The sound is there, and the voice dot is drawn there.
            _linePositions.Clear();
            foreach (FluxOutput v in _program.Voices)
            {
                _linePositions[v.Node] = transform.TransformPoint(ToVector(FluxSpace.VoicePosition(v.ReadMain(_registers), v.ReadTheta(_registers), v.ReadRadius(_registers), x)));
            }
            foreach (FluxOutput d in _program.Drums)
            {
                _linePositions[d.Node] = transform.TransformPoint(ToVector(FluxSpace.DrumPosition(d.ReadTheta(_registers))));
            }
            float gain = FluxHarpSettings.Gain;
            foreach (KeyValuePair<ushort, FluxHarpEmitter> pair in _emitters)
            {
                if (_linePositions.TryGetValue(pair.Key, out Vector3 at)) pair.Value.transform.position = at;
                pair.Value.Tick(gain);
            }

            PlaceCards(graph);
            Draw(graph, x, dj, beat);
        }

        private void PlaceCards(FluxGraph graph)
        {
            foreach (FluxNode node in graph.Nodes)
            {
                if (!_cards.TryGetValue(node.Id, out Card card)) continue;
                Vector3 local = ToVector(node.Position);
                if (_held != null && _held.Node == node.Id && Moving()) local = _board.InverseTransformPoint(_heldPoint + _grabOffset);
                else if (_remoteDrag.TryGetValue(node.Id, out Vector3 dragged)) local = dragged;
                card.Root.localPosition = local;
                card.Root.localRotation = FacingAxis(local);

                FluxKindInfo info = node.Info;
                bool dialling = _held != null && _held.Node == node.Id && Sideways();
                if (node.Kind == FluxKind.Const)
                {
                    SetText(card, 0, card.Name, "num");
                    SetText(card, 1, card.Live, FormatValue(dialling && _held.Role == RoleValue ? Dialled() : node.Value));
                }
                else if (node.Kind == FluxKind.Drum)
                {
                    double type = dialling && _held.Role == RoleValue ? Dialled() : node.Value;
                    SetText(card, 0, card.Name, "drum " + ((FluxDrumType)(int)type).ToString().ToLowerInvariant());
                    SetText(card, 1, card.Live, LiveOf(node));
                }
                else
                {
                    SetText(card, 1, card.Live, LiveOf(node));
                }

                for (int p = 0; p < info.Arity; p++)
                {
                    // A wired port reads its wire; its own number waits, hidden and out of reach, until it is cut.
                    bool own = node.Inputs[p] == FluxGraph.None;
                    card.LiteralGrips[p].enabled = own;
                    double literal = dialling && _held.Role == RoleLiteral && _held.Index == p ? Dialled() : node.Literals[p];
                    SetText(card, 2 + p, card.Literals[p], own ? FormatValue(literal) : "");
                }
            }
        }

        private string LiveOf(FluxNode node)
        {
            if (node.Info.IsOutput)
            {
                if (!FluxSound.TryFind(_program, node.Id, out FluxOutput line)) return "";
                double v = line.ReadMain(_registers);
                if (double.IsNaN(v)) return "-";
                return node.Kind == FluxKind.Voice ? $"{FluxSpace.Frequency(v):0.0}Hz" : FormatValue(v);
            }
            return _program.TryGetRegister(node.Id, out int r) ? FormatValue(_registers[r]) : "";
        }

        private static string FormatValue(double v)
        {
            if (double.IsNaN(v)) return "undef";
            if (double.IsInfinity(v)) return v > 0 ? "inf" : "-inf";
            double a = Math.Abs(v);
            if (a >= 1000) return v.ToString("0");
            if (a >= 10) return v.ToString("0.0");
            return v.ToString("0.###");
        }

        // ---- drawing ----------------------------------------------------------------------------

        private void Draw(FluxGraph graph, double x, int dj, double beat)
        {
            Transform eye = EyeTransform();
            if (eye == null) return;
            _glow.Begin(eye.position);
            DrawLines(x, dj, beat);
            DrawBoard(graph);
            DrawDj(dj, beat, graph.Transport);
            _glow.End();
            // The light is built in world space: its renderer stays at the world origin wherever the instrument stands.
            _glowObject.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        /// <summary>
        /// Each line's path over the coming beat, drawn as a fading ribbon from the voice dot onward, so the
        /// shape of the music is visible a moment before it is heard. Gaps (undefined) are gaps.
        /// </summary>
        private void DrawLines(double x, int dj, double beat)
        {
            Transform t = transform;
            const int Steps = 96;
            const double Ahead = 1.0;
            foreach (FluxOutput v in _program.Voices)
            {
                _pathPoints.Clear();
                _pathColors.Clear();
                double lastX = double.NegativeInfinity;
                for (int s = 0; s <= Steps; s++)
                {
                    double sx = FluxTransport.XOf(beat + Ahead * s / Steps);
                    bool wrapped = sx < lastX;
                    lastX = sx;
                    _program.Evaluate(v.Slice, sx, dj, _scratch);
                    double y = v.ReadMain(_scratch);
                    if (!IsFinite(y) || wrapped)
                    {
                        FlushPath();
                        if (!IsFinite(y)) continue;
                    }
                    FluxVec p = FluxSpace.VoicePosition(y, v.ReadTheta(_scratch), v.ReadRadius(_scratch), sx);
                    _pathPoints.Add(t.TransformPoint(ToVector(p)));
                    float fade = 1f - s / (float)Steps;
                    _pathColors.Add(new Color(1, 1, 1, 0.55f * fade * fade));
                }
                FlushPath();

                if (!double.IsNaN(v.ReadMain(_registers)) && _linePositions.TryGetValue(v.Node, out Vector3 at))
                {
                    _glow.Dot(at, 0.11f, White, 0.3f);
                }
            }

            foreach (FluxOutput d in _program.Drums)
            {
                if (!_linePositions.TryGetValue(d.Node, out Vector3 at)) continue;
                double env = d.ReadMain(_registers);
                float level = double.IsNaN(env) ? 0 : Mathf.Clamp01((float)env);
                _glow.Dot(at, 0.05f + 0.25f * level, new Color(1, 1, 1, 0.15f + 0.85f * level), 0.2f);
            }

            // The ring itself: the loop, with a tick per bar and the playhead on it.
            Vector3 c = t.TransformPoint(new Vector3(0, (float)FluxSpace.BaseHeight, 0));
            _glow.Circle(c, t.right, t.forward, (float)FluxSpace.RingRadius, 0.004f, new Color(1, 1, 1, 0.08f), 128);
            Color tick = new Color(1, 1, 1, 0.25f);
            for (int bar = 0; bar < FluxTransport.LoopBeats / FluxTransport.BeatsPerBar; bar++)
            {
                Vector3 w = t.TransformPoint(ToVector(FluxSpace.Polar(bar / 4.0, FluxSpace.RingRadius, FluxSpace.BaseHeight)));
                _glow.Segment(w - Vector3.up * 0.04f, w + Vector3.up * 0.04f, 0.004f, tick, tick);
            }
            Vector3 head = t.TransformPoint(ToVector(FluxSpace.Polar(FluxSpace.Playhead(x), FluxSpace.RingRadius, FluxSpace.BaseHeight)));
            _glow.Segment(head - Vector3.up * 0.9f, head + Vector3.up * 1.6f, 0.003f, new Color(1, 1, 1, 0), new Color(1, 1, 1, 0.22f));
        }

        private void FlushPath()
        {
            if (_pathPoints.Count >= 2) _glow.Strip(_pathPoints, _pathColors, 0.012f);
            _pathPoints.Clear();
            _pathColors.Clear();
        }

        private void DrawBoard(FluxGraph graph)
        {
            // Wires: output to input, bright where the value flowing is defined, dim where it is undefined.
            foreach (FluxNode node in graph.Nodes)
            {
                if (!_cards.TryGetValue(node.Id, out Card dst)) continue;
                for (int p = 0; p < node.Inputs.Length; p++)
                {
                    ushort src = node.Inputs[p];
                    if (src == FluxGraph.None || !_cards.TryGetValue(src, out Card from)) continue;
                    bool defined = _program.TryGetRegister(src, out int r) && !double.IsNaN(_registers[r]);
                    Wire(OutputWorld(from), from.Root.right, InputWorld(dst, p), dst.Root.right, new Color(1, 1, 1, defined ? 0.42f : 0.12f));
                }
            }

            foreach (KeyValuePair<ushort, Card> pair in _cards)
            {
                Card card = pair.Value;
                if (!graph.TryGet(pair.Key, out FluxNode node)) continue;
                bool hot = _hovered != null && _hovered.Node == pair.Key && _hovered.Role != RolePalette && _hovered.Role != RoleDj
                    || _held != null && _held.Node == pair.Key && _held.Role != RolePalette;
                DrawCard(card, node, hot);
            }

            foreach (Transform chip in _palette)
            {
                bool hot = _hovered != null && _hovered.Role == RolePalette && _hovered.transform == chip;
                Rect(chip, FluxCard.Width * 0.8f, FluxCard.Header, 0.0015f, new Color(1, 1, 1, hot ? 0.8f : 0.2f));
            }

            if (_held != null)
            {
                Vector3 from = _held.transform.position;
                switch (_held.Role)
                {
                    case RoleOutPort:
                        Wire(from, _held.transform.right, _heldPoint, _held.transform.right, White);
                        break;
                    case RolePort:
                        Wire(_heldPoint, _held.transform.right, from, _held.transform.right, new Color(1, 1, 1, 0.6f));
                        break;
                    case RoleLiteral when Pulled() && !Sideways() && !PulledDown():
                        // Becoming a number node: its card outline follows the hand, wired back to the port.
                        _glow.Ring(_heldPoint, 0.03f, 0.002f, White, 4);
                        Wire(_heldPoint, _held.transform.right, InputWorld(_cards[_held.Node], _held.Index), _held.transform.right, new Color(1, 1, 1, 0.6f));
                        break;
                    case RoleValue when !Sideways() && TryPortAt(_heldPoint, out ushort intoNode, out int intoPort) && intoNode != _held.Node:
                        // Over a port: letting go folds the number into it.
                        _glow.Ring(InputWorld(_cards[intoNode], intoPort), PortRadius * 1.4f, 0.003f, White, 16);
                        break;
                    case RolePalette:
                        _glow.Ring(_heldPoint, 0.04f, 0.003f, White, 24);
                        break;
                }
                if ((_held.Role == RolePalette || Moving()) && BoardLocal(_heldPoint + _grabOffset).Length > FluxGraph.BoardRadius)
                {
                    _glow.Ring(_heldPoint, 0.07f, 0.003f, new Color(1, 1, 1, 0.4f), 6); // outside the sphere: will be dropped
                }
            }

            if (Time.time < _flashUntil)
            {
                // An edit was refused: the board flickers once round its waist.
                float a = (_flashUntil - Time.time) / 1.5f;
                _glow.Circle(_board.position, _board.right, _board.forward, FluxCard.ReadingRadius, 0.004f, new Color(1, 1, 1, 0.7f * a), 96);
            }
        }

        private void DrawCard(Card card, FluxNode node, bool hot)
        {
            FluxKindInfo info = node.Info;
            Transform t = card.Root;
            float w = FluxCard.Width, h = FluxCard.Height(node.Kind);
            Color edge = new Color(1, 1, 1, hot ? 0.9f : info.IsOutput ? 0.6f : 0.32f);
            Rect(t, w, h, info.IsOutput ? 0.003f : 0.0018f, edge);
            float headerLine = h * 0.5f - FluxCard.Header;
            _glow.Segment(t.TransformPoint(new Vector3(-w * 0.5f, headerLine, 0)), t.TransformPoint(new Vector3(w * 0.5f, headerLine, 0)),
                0.0012f, new Color(1, 1, 1, 0.18f), new Color(1, 1, 1, 0.18f));

            for (int p = 0; p < info.Arity; p++)
            {
                bool wired = node.Inputs[p] != FluxGraph.None;
                Vector3 at = InputWorld(card, p);
                if (wired)
                {
                    _glow.Dot(at, PortRadius * 0.6f, new Color(1, 1, 1, 0.8f), 0.4f);
                    continue;
                }
                _glow.Ring(at, PortRadius * 0.4f, 0.0012f, new Color(1, 1, 1, 0.45f), 12);
                // The port's own number gets a faint underline: it can be held like a node.
                Vector3 l = new Vector3(FluxCard.Input(node.Kind, p).X + LiteralInset, FluxCard.Input(node.Kind, p).Y - FluxCard.Row * 0.38f, 0);
                bool lit = _hovered != null && _hovered.Role == RoleLiteral && _hovered.Node == node.Id && _hovered.Index == p;
                Color under = new Color(1, 1, 1, lit ? 0.8f : 0.16f);
                _glow.Segment(t.TransformPoint(l - Vector3.right * LiteralWidth * 0.5f), t.TransformPoint(l + Vector3.right * LiteralWidth * 0.5f), 0.001f, under, under);
            }
            if (!info.IsOutput) _glow.Dot(OutputWorld(card), PortRadius * 0.7f, new Color(1, 1, 1, 0.9f), 0.4f);
        }

        private void Rect(Transform t, float w, float h, float width, Color color)
        {
            Vector3 a = t.TransformPoint(new Vector3(-w * 0.5f, h * 0.5f, 0));
            Vector3 b = t.TransformPoint(new Vector3(w * 0.5f, h * 0.5f, 0));
            Vector3 c = t.TransformPoint(new Vector3(w * 0.5f, -h * 0.5f, 0));
            Vector3 d = t.TransformPoint(new Vector3(-w * 0.5f, -h * 0.5f, 0));
            _glow.Segment(a, b, width, color, color);
            _glow.Segment(b, c, width, color, color);
            _glow.Segment(c, d, width, color, color);
            _glow.Segment(d, a, width, color, color);
        }

        /// <summary>A wire leaves an output along its card's right and arrives at an input from its card's left.</summary>
        private void Wire(Vector3 a, Vector3 aRight, Vector3 b, Vector3 bRight, Color color)
        {
            float reach = Mathf.Min(0.12f, Vector3.Distance(a, b) * 0.45f);
            Vector3 a1 = a + aRight * reach, b1 = b - bRight * reach;
            _pathPoints.Clear();
            _pathColors.Clear();
            const int Steps = 16;
            for (int i = 0; i <= Steps; i++)
            {
                float s = i / (float)Steps, u = 1 - s;
                _pathPoints.Add(u * u * u * a + 3 * u * u * s * a1 + 3 * u * s * s * b1 + s * s * s * b);
                _pathColors.Add(color);
            }
            _glow.Strip(_pathPoints, _pathColors, 0.0025f);
            _pathPoints.Clear();
            _pathColors.Clear();
        }

        private void DrawDj(int dj, double beat, FluxTransport transport)
        {
            bool pending = transport.SwitchBeat > beat && transport.NextDj != transport.Dj;
            float barPhase = (float)(beat / FluxTransport.BeatsPerBar - Math.Floor(beat / FluxTransport.BeatsPerBar));
            for (int m = 0; m < FluxGraph.DjModes; m++)
            {
                Vector3 local = DjCubePosition(m);
                Vector3 c = transform.TransformPoint(local);
                Quaternion r = transform.rotation * Quaternion.Euler(20, 35 + (float)beat * (m == dj ? 22.5f : 4f), 0);
                bool active = m == dj;
                bool queued = pending && m == transport.NextDj;
                float alpha = active ? 1f : queued ? (barPhase > 0.5f ? 1f : 0.4f) : 0.25f;
                _glow.Cube(c, r, DjSize * (active ? 1f : 0.8f), active ? 0.005f : 0.0025f, new Color(1, 1, 1, alpha));
                // The mode is counted in dots, not named: one to four.
                Vector3 side = transform.rotation * FacingAxis(local) * Vector3.right;
                for (int k = 0; k <= m; k++)
                {
                    _glow.Dot(c + side * ((k - m * 0.5f) * 0.022f) - transform.up * (DjSize * 0.8f), 0.007f, new Color(1, 1, 1, alpha), 0.4f);
                }
            }
        }
    }
}
