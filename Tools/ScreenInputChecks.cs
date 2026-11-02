// Host-only tests of the actual input behaviour. Stubs model a flat quad and
// tracking ray; ClientSim is still required to verify SDK event delivery.
using System;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace UdonSharp
{
    public enum BehaviourSyncMode { None }
    public class UdonBehaviourSyncModeAttribute : Attribute
    { public UdonBehaviourSyncModeAttribute(BehaviourSyncMode mode) {} }
    public class UdonSharpBehaviour
    {
        public Transform transform = new Transform();
        public object component;
        public T GetComponent<T>() where T : class { return component as T; }
        public virtual void InputUse(bool value, UdonInputEventArgs args) {}
        public virtual void Interact() {}
    }
}
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float a, float b, float c) { x=a; y=b; z=c; }
        public static Vector3 forward = new Vector3(0,0,1);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x*b,a.y*b,a.z*b);
        public static float Dot(Vector3 a, Vector3 b) => a.x*b.x+a.y*b.y+a.z*b.z;
    }
    public struct Quaternion { public static Vector3 operator *(Quaternion a, Vector3 b) => b; }
    public struct Vector4 { public Vector4(float a,float b,float c,float d) {} }
    public class Transform
    {
        public Vector3 position;
        public Vector3 forward = Vector3.forward;
        public Vector3 InverseTransformPoint(Vector3 p) => p-position;
    }
    public class Material { public void SetVector(string key, Vector4 value) {} }
    public static class Mathf
    {
        public static float Abs(float f) => Math.Abs(f);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
    }
    public static class Debug { public static void Log(string s) {} }
}
namespace UnityEngine.UI { public class Text { public string text; } }
namespace VRC.Udon.Common
{
    public enum HandType { LEFT, RIGHT }
    public class UdonInputEventArgs { public HandType handType; }
}
namespace VRC.SDKBase
{
    public static class Networking { public static VRCPlayerApi LocalPlayer; }
    public static class Utilities { public static bool IsValid(object o) => o != null; }
    public class VRCPlayerApi
    {
        public enum TrackingDataType { RightHand, Head }
        public struct TrackingData { public Vector3 position; public Quaternion rotation; }
        public TrackingData tracking;
        public bool vr;
        public bool IsUserInVR() => vr;
        public TrackingData GetTrackingData(TrackingDataType type) => tracking;
    }
}
public class ScummV5Vm {}
public class ScummRoomBackground { public ScummV5Vm vm; }
class ScreenInputChecks
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Main()
    {
        var vm = new ScummV5Vm();
        var background = new ScummRoomBackground { vm = vm };
        var input = new ScummScreenInput { screen = new Transform(), screenMaterial = new Material(), component = background };
        var player = new VRCPlayerApi();
        Networking.LocalPlayer = player;
        player.tracking.position = new Vector3(0,0,-1);
        var right = new UdonInputEventArgs { handType = HandType.RIGHT };
        var left = new UdonInputEventArgs { handType = HandType.LEFT };
        input.InputUse(true, right);
        input.Interact();
        input.InputUse(true, right);
        Check(input.clickSequence == 1, "Use + Interact + repeated true must be one click");
        Check(input.clickX == 160 && input.clickY == 100 && input.clickVm == vm, "Capture coordinates and target");
        player.tracking.position = new Vector3(2,0,-1);
        input.InputUse(false, right);
        input.InputUse(true, right);
        Check(input.clickSequence == 1, "Off-screen Use must not click center");
        player.tracking.position = new Vector3(.25f,.25f,-1);
        input.InputUse(true, right);
        Check(input.clickSequence == 1, "Moving onto screen while held must not click");
        input.InputUse(false, right);
        input.InputUse(true, right);
        Check(input.clickSequence == 2 && input.clickX == 240 && input.clickY == 50, "Second press captures new coordinates");
        player.vr = true;
        input.InputUse(false, right);
        input.InputUse(true, left);
        Check(input.clickSequence == 2, "Ignore left hand in VR");
        input.InputUse(true, right);
        input.InputUse(false, left);
        input.InputUse(true, right);
        Check(input.clickSequence == 3, "Left release must not clear right-hand latch");
        var replacement = new ScummV5Vm();
        background.vm = replacement;
        Check(input.ReadTargetVm() == replacement && input.clickVm == vm, "Target switch must preserve previous click owner");
        input.InputUse(false, right);
        input.InputUse(true, right);
        Check(input.clickVm == replacement, "Next click goes to displayed VM");
        Console.WriteLine("PASS screen input ownership, coordinates, press deduplication and off-screen/VR filtering");
    }
}
