using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Rendering.Universal.Internal;

public class AfterLateUpdateSystem
{
    [RuntimeInitializeOnLoadMethod]
    private static void Initialize()
    {
        var playerLoop = PlayerLoop.GetDefaultPlayerLoop();
        if(CheckRegist(ref playerLoop))
            return;
        
        var afterLateUpdate = new PlayerLoopSystem()
        {
            type = typeof(AfterLateUpdateSystem),
            updateDelegate = () =>
            {
                // RenderSettingManger.Instance?.AfterLateUpdate();
                FastDynamicBoneManager.Instance?.AfterLateUpdate();
            },
        };

        var sysIndex = Array.FindIndex(playerLoop.subSystemList, (s) => s.type.Name == "PostLateUpdate");
        PlayerLoopSystem postLateSystem = playerLoop.subSystemList[sysIndex];
        var postLateSubsystemList = new List<PlayerLoopSystem>(postLateSystem.subSystemList);
        var index = postLateSubsystemList.FindIndex(h => h.type.Name.Contains("FinishFrameRendering"));
        postLateSubsystemList.Insert(index + 1, afterLateUpdate); //  LateUpdate() after
        postLateSystem.subSystemList = postLateSubsystemList.ToArray();
        playerLoop.subSystemList[sysIndex] = postLateSystem;
        PlayerLoop.SetPlayerLoop(playerLoop);
    }
    
    private static bool CheckRegist(ref PlayerLoopSystem playerLoop)
    {
        var t = typeof(AfterLateUpdateSystem);
        foreach (var subloop in playerLoop.subSystemList)
        {
            if (subloop.subSystemList != null && subloop.subSystemList.Any(x => x.type == t))
            {
                return true;
            }
        }
        return false;
    }
}