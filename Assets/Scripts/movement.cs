using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using UnityEngine;
using Valve.VR;
using Valve.VR.InteractionSystem;

public class movement : MonoBehaviour {
    public Transform player;
    public float speed = 1.0f;
    private UnityEngine.Vector3 handmove;
    public SteamVR_Action_Boolean m_GrabAction = null;
    private SteamVR_Behaviour_Pose m_Pose = null;

    void Awake() {
        m_Pose = GetComponent<SteamVR_Behaviour_Pose>();
    }
    void Update() {
        handmove = m_Pose.GetVelocity() * -1;

        if (m_GrabAction.GetState(m_Pose.inputSource))
        {
            UnityEngine.Debug.Log(handmove);
            UnityEngine.Vector3 controlled_vector = handmove * speed;
            controlled_vector[1] = 0.0f;
            player.position += controlled_vector;
            
        }
    }
}
