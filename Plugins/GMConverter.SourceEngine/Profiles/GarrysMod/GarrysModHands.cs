using System.Numerics;
using GMConverter.SDK.Geometry;

namespace GMConverter.SourceEngine.Profiles.GarrysMod;

/// <summary>
/// The arm skeleton Garry's Mod's player hands (<c>models/weapons/c_arms_*.mdl</c>) follow. A
/// <c>c_</c> view model carries these bones, named and parented exactly so, and animates them; the
/// hands model then copies their poses. Rest poses are those of <c>c_arms_citizen.mdl</c>, which
/// every stock hands model and <c>c_</c> weapon shares (its procedural ulna and wrist bones are left out).
/// </summary>
internal static class GarrysModHands
{
    private const float _metersPerInch = 0.0254f;

    public static Skeleton Skeleton { get; } = Create(
    [
        Bone("ValveBiped.Bip01_Spine4", -1, HumanoidBone.Chest, 0.000006f, 0.000000f, 0.000000f, -0.704280f, 0.063165f, -0.704280f, 0.063166f),
        Bone("ValveBiped.Bip01_L_Clavicle", 0, HumanoidBone.LeftClavicle, 2.033348f, 1.000767f, 1.937660f, 0.473368f, 0.528721f, 0.598273f, -0.372084f),
        Bone("ValveBiped.Bip01_L_UpperArm", 1, HumanoidBone.LeftUpperArm, 6.028143f, 0.000004f, 0.000000f, -0.674987f, 0.220898f, -0.186740f, 0.678767f),
        Bone("ValveBiped.Bip01_L_Forearm", 2, HumanoidBone.LeftForearm, 11.692554f, -0.000000f, 0.000000f, 0.000000f, -0.000000f, -0.030181f, 0.999544f),
        Bone("ValveBiped.Bip01_L_Hand", 3, HumanoidBone.LeftHand, 11.481676f, -0.000000f, -0.000033f, 0.707603f, -0.022002f, 0.053294f, 0.704254f),
        Bone("ValveBiped.Bip01_L_Finger4", 4, HumanoidBone.LeftPinky1, 3.859703f, -0.142403f, -1.191974f, -0.177863f, 0.165141f, -0.397339f, 0.884994f),
        Bone("ValveBiped.Bip01_L_Finger41", 5, HumanoidBone.LeftPinky2, 1.312563f, -0.000004f, -0.000001f, 0.000000f, -0.000000f, -0.218093f, 0.975928f),
        Bone("ValveBiped.Bip01_L_Finger42", 6, HumanoidBone.LeftPinky3, 0.729366f, -0.000002f, 0.000000f, 0.000000f, -0.000000f, -0.121846f, 0.992549f),
        Bone("ValveBiped.Bip01_L_Finger3", 4, HumanoidBone.LeftRing1, 3.942326f, 0.046769f, -0.431443f, -0.055228f, 0.063418f, -0.390523f, 0.916744f),
        Bone("ValveBiped.Bip01_L_Finger31", 8, HumanoidBone.LeftRing2, 1.539093f, 0.000002f, 0.000000f, 0.000000f, -0.000000f, -0.123582f, 0.992334f),
        Bone("ValveBiped.Bip01_L_Finger32", 9, HumanoidBone.LeftRing3, 1.196322f, 0.000000f, -0.000000f, 0.000000f, -0.000000f, -0.213739f, 0.976891f),
        Bone("ValveBiped.Bip01_L_Finger2", 4, HumanoidBone.LeftMiddle1, 3.883690f, -0.046767f, 0.431443f, 0.044253f, -0.002002f, -0.289883f, 0.956036f),
        Bone("ValveBiped.Bip01_L_Finger21", 11, HumanoidBone.LeftMiddle2, 1.719578f, -0.000002f, 0.000000f, 0.000503f, 0.002712f, -0.182214f, 0.983255f),
        Bone("ValveBiped.Bip01_L_Finger22", 12, HumanoidBone.LeftMiddle3, 1.209183f, -0.000002f, 0.000000f, 0.000167f, 0.001533f, -0.107989f, 0.994151f),
        Bone("ValveBiped.Bip01_L_Finger1", 4, HumanoidBone.LeftIndex1, 3.859856f, -0.137946f, 1.332466f, 0.164538f, -0.066609f, -0.254928f, 0.950527f),
        Bone("ValveBiped.Bip01_L_Finger11", 14, HumanoidBone.LeftIndex2, 1.719431f, -0.000002f, -0.000001f, 0.000368f, 0.002004f, -0.180509f, 0.983571f),
        Bone("ValveBiped.Bip01_L_Finger12", 15, HumanoidBone.LeftIndex3, 1.099672f, 0.000000f, 0.000000f, 0.000158f, 0.001288f, -0.121862f, 0.992546f),
        Bone("ValveBiped.Bip01_L_Finger0", 4, HumanoidBone.LeftThumb1, 0.806046f, -0.348923f, 1.321258f, -0.604453f, -0.043883f, -0.473211f, 0.639361f),
        Bone("ValveBiped.Bip01_L_Finger01", 17, HumanoidBone.LeftThumb2, 1.789782f, -0.000002f, 0.000000f, 0.000000f, -0.000000f, 0.114013f, 0.993479f),
        Bone("ValveBiped.Bip01_L_Finger02", 18, HumanoidBone.LeftThumb3, 1.207001f, 0.000000f, 0.000002f, 0.000000f, -0.000000f, 0.180436f, 0.983587f),
        Bone("ValveBiped.Bip01_R_Clavicle", 0, HumanoidBone.RightClavicle, 2.033356f, 1.000772f, -1.937610f, -0.459704f, -0.549862f, 0.578901f, -0.388841f),
        Bone("ValveBiped.Bip01_R_UpperArm", 20, HumanoidBone.RightUpperArm, 6.028142f, 0.000004f, 0.000000f, 0.698925f, -0.214051f, -0.194551f, 0.654092f),
        Bone("ValveBiped.Bip01_R_Forearm", 21, HumanoidBone.RightForearm, 11.692551f, 0.000000f, -0.000003f, -0.000000f, -0.000000f, -0.030181f, 0.999544f),
        Bone("ValveBiped.Bip01_R_Hand", 22, HumanoidBone.RightHand, 11.481697f, 0.000000f, -0.000002f, -0.704694f, 0.022219f, 0.053200f, 0.707165f),
        Bone("ValveBiped.Bip01_R_Finger4", 23, HumanoidBone.RightPinky1, 3.859674f, -0.132568f, 1.193108f, 0.175736f, -0.167486f, -0.396103f, 0.885532f),
        Bone("ValveBiped.Bip01_R_Finger41", 24, HumanoidBone.RightPinky2, 1.312561f, 0.000000f, 0.000001f, 0.000000f, 0.000000f, -0.218069f, 0.975933f),
        Bone("ValveBiped.Bip01_R_Finger42", 25, HumanoidBone.RightPinky3, 0.729367f, 0.000000f, 0.000000f, 0.000000f, 0.000000f, -0.121832f, 0.992551f),
        Bone("ValveBiped.Bip01_R_Finger3", 23, HumanoidBone.RightRing1, 3.942293f, 0.050322f, 0.431043f, 0.053290f, -0.065802f, -0.389968f, 0.916927f),
        Bone("ValveBiped.Bip01_R_Finger31", 27, HumanoidBone.RightRing2, 1.539099f, -0.000002f, 0.000000f, 0.000000f, 0.000000f, -0.123569f, 0.992336f),
        Bone("ValveBiped.Bip01_R_Finger32", 28, HumanoidBone.RightRing3, 1.196323f, 0.000000f, -0.000000f, 0.000000f, 0.000000f, -0.213719f, 0.976895f),
        Bone("ValveBiped.Bip01_R_Finger2", 23, HumanoidBone.RightMiddle1, 3.883657f, -0.050331f, -0.431043f, -0.044915f, -0.000188f, -0.289887f, 0.956006f),
        Bone("ValveBiped.Bip01_R_Finger21", 30, HumanoidBone.RightMiddle2, 1.719582f, 0.000000f, -0.000000f, -0.000726f, -0.003920f, -0.182193f, 0.983255f),
        Bone("ValveBiped.Bip01_R_Finger22", 31, HumanoidBone.RightMiddle3, 1.209185f, -0.000002f, 0.000000f, -0.000241f, -0.002217f, -0.107977f, 0.994151f),
        Bone("ValveBiped.Bip01_R_Finger1", 23, HumanoidBone.RightIndex1, 3.859827f, -0.148943f, -1.331282f, -0.165063f, 0.064645f, -0.255430f, 0.950437f),
        Bone("ValveBiped.Bip01_R_Finger11", 33, HumanoidBone.RightIndex2, 1.719427f, -0.000002f, 0.000001f, -0.000597f, -0.003252f, -0.180489f, 0.983571f),
        Bone("ValveBiped.Bip01_R_Finger12", 34, HumanoidBone.RightIndex3, 1.099663f, -0.000002f, 0.000000f, -0.000257f, -0.002090f, -0.121852f, 0.992546f),
        Bone("ValveBiped.Bip01_R_Finger0", 23, HumanoidBone.RightThumb1, 0.806017f, -0.359836f, -1.318334f, 0.603807f, 0.040558f, -0.473599f, 0.639903f),
        Bone("ValveBiped.Bip01_R_Finger01", 36, HumanoidBone.RightThumb2, 1.789783f, 0.000002f, -0.000002f, 0.000000f, -0.000000f, 0.114029f, 0.993477f),
        Bone("ValveBiped.Bip01_R_Finger02", 37, HumanoidBone.RightThumb3, 1.207001f, 0.000000f, -0.000002f, 0.000000f, -0.000000f, 0.180458f, 0.983583f),
    ]);

    // Bones are listed parents first; each takes its place in the list as its index.
    private static Skeleton Create(Bone[] bones) => new([.. bones.Select((bone, index) => bone with { Index = index })]);

    private static Bone Bone(string name, int parent, HumanoidBone role, float x, float y, float z, float qx, float qy, float qz, float qw)
    {
        return new Bone(
            0,
            name,
            parent,
            new Transform(new Vector3(x, y, z) * _metersPerInch, new Quaternion(qx, qy, qz, qw), Vector3.One),
            role);
    }
}
