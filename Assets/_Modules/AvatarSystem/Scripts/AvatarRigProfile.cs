using System;
using UnityEngine;

[CreateAssetMenu(menuName = "MANGOs Framework/Avatar Rig Profile")]
public sealed class AvatarRigProfile : ScriptableObject
{
    public string urlPrefix;
    public Bone[] bones = Array.Empty<Bone>();
    [Serializable] public struct Bone { public HumanBodyBones humanBone; public string nodePath; }
}
