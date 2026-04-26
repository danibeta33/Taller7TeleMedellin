using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

[System.Serializable]
public class VotingCycleConfig
{
    public VideoClip intermediateVideo;
    public List<VotingOption> votingOptions = new List<VotingOption>();
}