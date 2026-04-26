using UnityEngine;
using UnityEngine.Video;

[System.Serializable]
public class CycleData
{
    public VideoClip introVideo;

    public VideoClip optionA;
    public VideoClip optionB;
    public VideoClip optionC;

    public string labelA = "Opcion A";
    public string labelB = "Opcion B";
    public string labelC = "Opcion C";
}