using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

public class AnimateObject : MonoBehaviour
{
    #region Animation Control
    public void Animate(string animationName)
    {
        if (!TryGetAnimationInfo(animationName, out var info))
        { Debug.LogWarning("Animation named: \"" + animationName + "\" does not exist!", gameObject); return; }

        if (IsAnimating) StopCoroutine(animating);
        animating = StartCoroutine(Animating(info));
    }

    public bool IsAnimating { get; private set; } = false;
    private Coroutine animating = null;
    private IEnumerator Animating(AnimationInfo info)
    {
        IsAnimating = true;
        onStart?.Invoke();
        info.onStart?.Invoke();
        AnimationInfo.Node lastNode = info.SnapToStart ? info.GetNode(0) :
            new AnimationInfo.Node(parent.position, parent.rotation, parent.localScale, parent.position);
        for (int i = 1; i < info.Nodes.Count; i++)
        {
            float timer = 0;
            AnimationInfo.Node currentNode = info.GetNode(i);
            while (timer < 1)
            {
                float speed = currentNode.AnimateOverride ? currentNode.SpeedOverride : info.Speed;
                timer += Time.deltaTime * speed;


                AnimationCurve curve = currentNode.AnimateOverride ? currentNode.CurveOverride : info.Curve;
                float curveTime = currentNode.AnimateOverride ? timer :
                    Mathf.Lerp((i - 1) / (float)info.Nodes.Count, i / (float)info.Nodes.Count, timer);
                float inverseLow = curve.Evaluate((i - 1) / (float)info.Nodes.Count);
                float inverseHigh = curve.Evaluate(i / (float)info.Nodes.Count);
                
                parent.SetPositionAndRotation(
                    GetPointAtTime(lastNode, currentNode, 
                        Mathf.InverseLerp(inverseLow, inverseHigh, curve.Evaluate(curveTime))), 
                    Quaternion.Slerp(lastNode.Rotation, currentNode.Rotation, 
                        Mathf.InverseLerp(inverseLow, inverseHigh, curve.Evaluate(curveTime))));
                parent.localScale = Vector3.Lerp(lastNode.Scale, currentNode.Scale, curve.Evaluate(timer));
                yield return null;
            }
            parent.SetPositionAndRotation(currentNode.Position, currentNode.Rotation);
            parent.localScale = currentNode.Scale;
            lastNode = currentNode;
        }
        info.onEnd?.Invoke();
        onEnd?.Invoke();
        IsAnimating = false;
    }
    private Vector3 GetPointAtTime(AnimationInfo.Node lastNode, AnimationInfo.Node currentNode, float time)
    {
        //P(t) = (1 - t)^3 * P0 + 3(1 - t)^2 * t * P1 + 3(1 - t) * t^2 * P2 + t^3 * P3
        return Mathf.Pow(1 - time, 3) * lastNode.Position + 3 * Mathf.Pow(1 - time, 2) * time * lastNode.ForwardBezier + 3 * (1 - time) * Mathf.Pow(time, 2) * currentNode.BackBezier + Mathf.Pow(time, 3) * currentNode.Position;
    }
    #endregion


    private static AnimationInfo.Node NEW_NODE = new AnimationInfo.Node(Vector3.zero, Quaternion.identity, Vector3.one, Vector3.zero);

    [SerializeField] private Transform parent;
    [SerializeField] private Mesh refMesh;
    [SerializeField] private UnityEvent onStart;
    [SerializeField] private UnityEvent onEnd;

    [System.Serializable] 
    public struct AnimationInfo
    {
        #region Identity
        public void SetKey(string k) { Key = k; }
        public string Key;
        #endregion
        public UnityEvent onStart;
        public UnityEvent onEnd;


        public AnimationInfo(AnimationInfo other)
        { 
            Nodes = new List<Node>(); 
            Key = other.Key; SnapToStart = other.SnapToStart; CurrentNode = other.CurrentNode;
            Speed = other.Speed;
            Curve = other.Curve;
            onStart = other.onStart;
            onEnd = other.onEnd;
            CopyNodes(other.Nodes); 
        }
        public AnimationInfo(List<Node> nodes) 
        { 
            Nodes = new List<Node>(); 
            Key = ""; SnapToStart = false; CurrentNode = 0;
            Speed = 1;
            Curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            onStart = null;
            onEnd = null;
            CopyNodes(nodes); 
        }

        public void SetSnapToStart(bool snapToStart) { SnapToStart = snapToStart; }
        public bool SnapToStart;
        public void SetCurrentNode(int currentNode) { CurrentNode = currentNode; }
        private void CheckCurrentNode()
        {
            if (CurrentNode < 0) { CurrentNode = 0; return; }
            if (CurrentNode >= Nodes.Count) { CurrentNode = Nodes.Count - 1; return; }
        }
        public int CurrentNode;
        public void SetSpeed(float speed)
        {
            Speed = speed;
            if (Speed <= 0) Speed = 1;
        }
        public float Speed;
        public void SetCurve(AnimationCurve curve)
        { Curve = curve; }
        public AnimationCurve Curve;


        #region Read Functions
        public readonly bool ValidIndex(int index) { return !(Nodes == null || index < 0 || index >= Nodes.Count); }
        public List<Node> Nodes;
        public readonly string[] GetNodeNames()
        {
            if (Nodes == null) return new string[0];
            string[] output = new string[Nodes.Count];
            for (int i = 0; i < Nodes.Count; i++)
            { output[i] = "Node " + (i + 1).ToString(); }
            return output;
        }
        #endregion

        #region Modify Functions
        public void CopyNodes(List<Node> nodes)
        {
            if (nodes == null)
            {
                Nodes.Add(new Node(NEW_NODE));
                return;
            }
            Nodes = new List<Node>(nodes); 
        }
        public readonly Node GetNode(int index)
        {
            if (!ValidIndex(index)) return new Node();
            return new Node(Nodes[index]);
        }
        public readonly bool SetNode(Node node, int index)
        {
            if (!ValidIndex(index)) return false;
            Nodes[index] = node; return true;
        }
        public readonly bool AddNode(Node newNode, int index = -1)
        {
            if (index == -1) { Nodes.Add(newNode); return true; }
            if (!ValidIndex(index)) return false;
            Nodes.Insert(index, newNode); return true;
        }
        public bool RemoveNode(int index)
        {
            if (!ValidIndex(index)) return false;
            if (Nodes.Count <= 1) return false;
            Nodes.RemoveAt(index);
            CheckCurrentNode();
            return true;
        }
        #endregion

        [System.Serializable]
        public struct Node
        {
            public Node(Node other)
            { 
                Position = other.Position; Rotation = other.Rotation; Scale = other.Scale; 
                BackBezier = other.BackBezier; ForwardBezier = other.ForwardBezier;
                AnimateOverride = other.AnimateOverride;
                SpeedOverride = other.SpeedOverride;
                CurveOverride = new AnimationCurve(other.CurveOverride.keys);
            }
            public Node(Vector3 position, Quaternion rotation, Vector3 scale, Vector3 bezier)
            { 
                Position = position; Rotation = rotation; Scale = scale; 
                BackBezier = bezier; ForwardBezier = bezier;
                AnimateOverride = false;
                SpeedOverride = 1;
                CurveOverride = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            }

            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
            public void SetPosition(Vector3 position) { Position = position; }
            public void SetRotation(Quaternion rotation) { Rotation = rotation; }
            public void SetScale(Vector3 scale) { Scale = scale; }

            public Vector3 BackBezier;
            public Vector3 ForwardBezier;
            public void SetBackBezier(Vector3 position) { BackBezier = position; }
            public void SetForwardBezier(Vector3 position) { ForwardBezier = position; }

            public bool AnimateOverride;
            public float SpeedOverride;
            public AnimationCurve CurveOverride;
            public void SetAnimateOverride(bool animateOverride) { AnimateOverride = animateOverride; }
            public void SetSpeedOverride(float speedOverride) { SpeedOverride = speedOverride; }
            public void SetCurveOverride(AnimationCurve curveOverride) { CurveOverride = curveOverride; }
        }
    }

    #region Private SavePositions

    public List<AnimationInfo> Animations = new List<AnimationInfo>();
    public bool ContainsKey(string key)
    {
        foreach (var info in Animations) if (info.Key == key) return true;
        return false;
    }

    public void Add(string key, AnimationInfo info)
    {
        if (ContainsKey(key)) return;
        Animations.Add(info);
    }
    public bool Remove(string key)
    {
        for (int i = 0; i < Animations.Count; i++)
        { if (Animations[i].Key == key) { Animations.RemoveAt(i); return true; } }
        return false;
    }
    public bool TryGetValue(string key, out AnimationInfo info)
    {
        info = new AnimationInfo();
        foreach (var i in Animations)
            if (i.Key == key) { info = i; return true; }
        return false;
    }

    public int Count { get { return Animations.Count; } }
    public string[] Keys
    {
        get
        {
            string[] output = new string[Animations.Count]; int i = 0;
            foreach (var info in Animations) output[i++] = info.Key;
            return output;
        }
    }
    #endregion

    #region Animation Library Functionality

    public bool AddAnimationInfo(string key, AnimationInfo info)
    {
        if (key == "") return false;
        if (ContainsKey(key)) return false; 
        info.SetKey(key); 
        Add(key, info);
        if (Animations.Count == 1) SetCurrentAnimation(key);
        return true;
    }
    public bool AddAnimationInfo(AnimationInfo info)
    {
        if (info.Key == "") { info.SetKey(GetEmptyKeyName()); }
        if (info.Key == "") 
        { Debug.LogError("Having an issue adding new key name. Try changing the name of one of your \"New Animation\" Animations."); return false; }
        if (Animations.Count > 0 && ContainsKey(info.Key)) return false;
        Add(info.Key, info);
        SetCurrentAnimation(info.Key);
        return true;
    }

    private void UpdateParentToCurrentPosition()
    {
        if (Application.isPlaying) return;
        if (!TryGetCurrentAnimation(out var current)) return;
        if (parent == null) return;
        parent.position = current.GetNode(current.CurrentNode).Position;
        parent.rotation = current.GetNode(current.CurrentNode).Rotation;
        parent.localScale = current.GetNode(current.CurrentNode).Scale;
    }
    public bool SaveAnimationInfo(AnimationInfo info)
    {
        if (info.Key == "") return false;
        if (Animations.Count <= 0 || !ContainsKey(info.Key)) return false;
        Remove(info.Key);
        Add(info.Key, info);
        UpdateParentToCurrentPosition();
        return true;
    }
    public bool TryGetCurrentAnimation(out AnimationInfo info)
    {
        return TryGetValue(CurrentAnimation, out info); 
    }
    public bool TryGetAnimationInfo(string key, out AnimationInfo info)
    {
        info = new AnimationInfo(null);
        //if (Animations == null) return false;
        return TryGetValue(key, out info); 
    }
    public bool RemoveAnimationInfo(string key)
    {
        //if (Animations == null) return false;
        return Remove(key); 
    }
    public bool RemoveAnimationInfo(AnimationInfo info)
    {
        //if (Animations == null) return false;
        return Remove(info.Key); 
    }

    public bool ChangeAnimationName(string from, string to)
    {
        if (from == "" || to == "") return false;
        //if (Animations == null) return false;
        if (ContainsKey(to)) return false;
        if (!TryGetAnimationInfo(from, out AnimationInfo info)) return false;
        info.SetKey(to);
        Remove(from);
        Add(to, info);
        if (from == CurrentAnimation) SetCurrentAnimation(to);
        return true;
    }

    public void NewNode()
    {
        if (!TryGetCurrentAnimation(out AnimationInfo info)) return;
        info.AddNode(new AnimationInfo.Node(NEW_NODE));
    }

    private string GetEmptyKeyName()
    {
        if (Animations.Count <= 0) return "New Animation 1";
        for (int i = 1; i <= Animations.Count + 1; i++)
        {
            string keyName = "New Animation " + i.ToString();
            if (!ContainsKey(keyName)) return keyName;
        }
        return "";
    }
    public int GetAnimationNames(out string[] names)
    {
        int outputPos = 0;
        names = new string[Animations.Count];
        int i = 0;
        foreach (var key in Keys)
        {
            if (key == CurrentAnimation) outputPos = i;
            names[i++] = key;
        }
        return outputPos;
    }
    public string CurrentAnimation { get; private set; } = "";
    public void SetCurrentAnimation(string animationKey) 
    { CurrentAnimation = animationKey; }
    #endregion

    #region Gizmos
    private const float NODE_SPHERE_SIZE_SCALER = 1;
    private const float NODE_BEZIER_HANDLE_SIZE = 0.3f;
    private const int NODE_BEZIER_TIME_STEPS = 10;

    private void OnDisable()
    {
        Tools.hidden = false;
    }
    private void OnDrawGizmosSelected()
    {
        Tools.hidden = true;
        DrawCurrentAnimation();
    }
    private void DrawCurrentAnimation()
    {
        if (refMesh == null) return;
        if (!TryGetCurrentAnimation(out AnimationInfo info)) return;
        if (info.Nodes.Count <= 0) return;

        var currentNode = info.GetNode(0);
        Vector3 fromPos = currentNode.Position;
        for (int i = 0; i < info.Nodes.Count; i++)
        {
            currentNode = info.GetNode(i); 

            Gizmos.color = i == info.CurrentNode ? Color.green : Color.white;
            Gizmos.DrawWireMesh(refMesh, 0, currentNode.Position, currentNode.Rotation, currentNode.Scale * NODE_SPHERE_SIZE_SCALER);

            DrawBezierLine(fromPos, currentNode.Position, info.GetNode(i - 1).ForwardBezier, currentNode.BackBezier);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(currentNode.BackBezier, NODE_BEZIER_HANDLE_SIZE);
            Gizmos.DrawLine(currentNode.BackBezier, currentNode.Position);
            if (i != info.Nodes.Count - 1)
            {
                Gizmos.DrawWireSphere(currentNode.ForwardBezier, NODE_BEZIER_HANDLE_SIZE);
                Gizmos.DrawLine(currentNode.ForwardBezier, currentNode.Position);
            }

            fromPos = currentNode.Position;
        }
    }

    private void DrawBezierLine(Vector3 from, Vector3 to, Vector3 bezier1, Vector3 bezier2)
    {
        if (from == to) return;
        Gizmos.color = Color.blue;

        Vector3 lastLinePoint = from;
        for (int i = 1; i <= NODE_BEZIER_TIME_STEPS; i++)
        {
            float t = Mathf.Lerp(0f, 1f, i / (float)NODE_BEZIER_TIME_STEPS);
            //P(t) = (1 - t)^3 * P0 + 3(1 - t)^2 * t * P1 + 3(1 - t) * t^2 * P2 + t^3 * P3
            Vector3 newLinePoint = Mathf.Pow(1 - t, 3) * from + 3 * Mathf.Pow(1 - t, 2) * t * bezier1 + 3 * (1 - t) * Mathf.Pow(t, 2) * bezier2 + Mathf.Pow(t, 3) * to;

            Gizmos.DrawLine(lastLinePoint, newLinePoint);
            lastLinePoint = newLinePoint;
        }
    }
    #endregion
}

[CustomEditor(typeof(AnimateObject))]
class AnimateObjectTool : Editor
{
    public override void OnInspectorGUI()
    {
        AnimateObject instance = target as AnimateObject;

        base.OnInspectorGUI();

        if (!PrintAnimationControls(instance)) return;

        PrintNodeControls(instance);
    }

    private void PrintHeader(string text)
    {
        EditorGUILayout.Separator();
        EditorGUILayout.LabelField(text);
    }

    private bool PrintAnimationControls(AnimateObject instance)
    {
        PrintHeader("Animation Control");

        if (GUILayout.Button("New Animation"))
            instance.AddAnimationInfo(new AnimateObject.AnimationInfo(null));

        int current = instance.GetAnimationNames(out string[] animationNames);
        if (animationNames.Length <= 0) return false;

        int selected = EditorGUILayout.Popup("Selected Animation", current, animationNames);
        instance.SetCurrentAnimation(animationNames[selected]);

        if (GUILayout.Button("Remove Current Animation"))
            instance.RemoveAnimationInfo(instance.CurrentAnimation);

        string editName = EditorGUILayout.TextField("Animation Name", instance.CurrentAnimation);
        if (editName != instance.CurrentAnimation) 
            instance.ChangeAnimationName(instance.CurrentAnimation, editName);

        if (instance.TryGetCurrentAnimation(out AnimateObject.AnimationInfo info))
        { info.SetSnapToStart(EditorGUILayout.Toggle("Snap To Start", info.SnapToStart)); }

        float newSpeed = EditorGUILayout.FloatField("Animation Speed", info.Speed);
        if (newSpeed != info.Speed) { info.SetSpeed(newSpeed); }

        AnimationCurve newCurve = EditorGUILayout.CurveField("Animation Curve", info.Curve);
        { info.SetCurve(newCurve); }

        instance.SaveAnimationInfo(info);
        return true;
    }

    private void PrintNodeControls(AnimateObject instance)
    {
        EditorGUILayout.Separator();
        EditorGUILayout.LabelField("Node Control");

        if (GUILayout.Button("New Node"))
            instance.NewNode();

        if (instance.TryGetCurrentAnimation(out AnimateObject.AnimationInfo info))
        {
            string[] nodes = info.GetNodeNames();
            if (nodes.Length <= 0) return;

            int selected = EditorGUILayout.Popup("Selected Node", info.CurrentNode, nodes);
            if (selected != info.CurrentNode)
            { info.SetCurrentNode(selected); }

            if (GUILayout.Button("Remove Selected Node"))
            { info.RemoveNode(info.CurrentNode); }

            var node = info.GetNode(info.CurrentNode);

            node.SetAnimateOverride(EditorGUILayout.Toggle("Animation Override", node.AnimateOverride));
            node.SetSpeedOverride(EditorGUILayout.FloatField("Speed Override", node.SpeedOverride));
            node.SetCurveOverride(EditorGUILayout.CurveField("Animation Curve", node.CurveOverride));

            info.SetNode(node, info.CurrentNode);

            instance.SaveAnimationInfo(info);
        }
    }

    public void OnSceneGUI()
    {
        var instance = target as AnimateObject;
        if (!instance.TryGetCurrentAnimation(out AnimateObject.AnimationInfo currentAnimation)) return;
        

        EditorGUI.BeginChangeCheck();

        if (currentAnimation.Nodes == null) return;
        for (int i = 0; i < currentAnimation.Nodes.Count; i++)
        { 
            var node = currentAnimation.GetNode(i);
            switch (Tools.current)
            {
                case Tool.Move:
                    if (DrawNodeMoveHandle(ref node))
                        currentAnimation.SetNode(node, i);
                    if (DrawNodeBezierHandle(ref node))
                        currentAnimation.SetNode(node, i);
                    break;
                case Tool.Rotate:
                    if (DrawNodeRotateHandle(ref node))
                        currentAnimation.SetNode(node, i);
                    break;
                case Tool.Scale:
                    if (DrawNodeScaleHandle(ref node))
                        currentAnimation.SetNode(node, i);
                    break;
                case Tool.Rect:
                    if (Handles.Button(node.Position, node.Rotation, 1, 1, Handles.SphereHandleCap))
                        currentAnimation.RemoveNode(i);
                    break;
            }
        }
        instance.SaveAnimationInfo(currentAnimation);
    }

    private bool DrawNodeMoveHandle(ref AnimateObject.AnimationInfo.Node node)
    {
        Vector3 position = Handles.PositionHandle(node.Position, Quaternion.identity);
        if (EditorGUI.EndChangeCheck())
        {
            //Undo.RecordObject(target, "Move point");
            node.SetPosition(position);
            return true;
        }
        return false;
    }
    private bool DrawNodeBezierHandle(ref AnimateObject.AnimationInfo.Node node)
    {
        Vector3 backPosition = Handles.PositionHandle(node.BackBezier, Quaternion.identity);
        Vector3 forwardPosition = Handles.PositionHandle(node.ForwardBezier, Quaternion.identity);
        if (EditorGUI.EndChangeCheck())
        {
            //Undo.RecordObject(target, "Move point");
            node.SetBackBezier(backPosition);
            node.SetForwardBezier(forwardPosition);
            return true;
        }
        return false;
    }
    private bool DrawNodeRotateHandle(ref AnimateObject.AnimationInfo.Node node)
    {
        Quaternion rotation = Handles.RotationHandle(node.Rotation, node.Position);
        if (EditorGUI.EndChangeCheck())
        {
            //Undo.RecordObject(target, "Move point");
            node.SetRotation(rotation);
            return true;
        }
        return false;
    }
    private bool DrawNodeScaleHandle(ref AnimateObject.AnimationInfo.Node node)
    {
        Vector3 scale = Handles.ScaleHandle(node.Scale, node.Position, node.Rotation);
        if (EditorGUI.EndChangeCheck())
        {
            //Undo.RecordObject(target, "Move point");
            node.SetScale(scale);
            return true;
        }
        return false;
    }
}
