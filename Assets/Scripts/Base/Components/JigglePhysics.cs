using System;
using System.Collections.Generic;
using NikkeViewerEX.Serialization;
using UnityEngine;

namespace NikkeViewerEX.Components
{
    public class JigglePhysics
    {
        public class BoneHandle
        {
            public Func<float> GetRotation;
            public Action<float> SetRotation;
            public Func<float> GetX;
            public Func<float> GetY;
            public Action<float> SetX;
            public Action<float> SetY;
        }

        class BoneState
        {
            public BoneHandle Handle;
            public float RotVelocity;
            public float RotDisplacement;
            public float PosVelY;
            public float PosDispY;
            public float OriginalRotation;
            public float OriginalY;
            public bool BoneSet;
        }

        readonly Dictionary<string, BoneState> states = new();
        readonly List<JiggleBoneSettings> boneSettings;
        bool initialized;
        Vector2 prevMouseNorm;
        float prevMouseSpeed;

        public JigglePhysics(List<JiggleBoneSettings> boneSettings)
        {
            this.boneSettings = boneSettings;
        }

        public void AddBone(string name, BoneHandle handle)
        {
            states[name] = new BoneState { Handle = handle };
            initialized = true;
        }

        public void SetInitialMouse(Vector2 normalizedMouse)
        {
            prevMouseNorm = normalizedMouse;
            prevMouseSpeed = 0f;
        }

        public void Update(Vector2 normalizedMouse)
        {
            if (!initialized) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector2 mouseDelta = normalizedMouse - prevMouseNorm;
            prevMouseNorm = normalizedMouse;

            // Mouse acceleration (change in speed) is the impulse trigger
            // Constant speed = no new impulse, only speed changes kick the spring
            float mouseSpeed = mouseDelta.magnitude / dt;
            float mouseAccel = Mathf.Abs(mouseSpeed - prevMouseSpeed);
            prevMouseSpeed = mouseSpeed;

            foreach (var s in boneSettings)
            {
                if (!states.TryGetValue(s.BoneName, out var state)) continue;

                // On first frame, cache the animation's original values
                if (!state.BoneSet)
                {
                    state.OriginalRotation = state.Handle.GetRotation();
                    state.OriginalY = state.Handle.GetY();
                    state.BoneSet = true;
                }

                // --- Rotation: damped spring ---
                // F = -kx - cv  (classic spring-damper, always returns to zero)
                float rotForce = -s.Stiffness * state.RotDisplacement
                                 - s.Damping * state.RotVelocity;
                state.RotVelocity += rotForce * dt;
                // Mouse acceleration kicks velocity (direction from horizontal mouse delta)
                float rotKick = s.ForceFactor * mouseAccel * Mathf.Sign(mouseDelta.x + 0.001f);
                state.RotVelocity += rotKick * dt;
                state.RotDisplacement += state.RotVelocity * dt;
                state.RotDisplacement = Mathf.Clamp(
                    state.RotDisplacement, -s.MaxRotDisplacement, s.MaxRotDisplacement
                );

                // --- Position Y: damped spring, gravity direction ---
                float posForce = -s.PosStiffness * state.PosDispY
                                 - s.PosDamping * state.PosVelY;
                state.PosVelY += posForce * dt;
                // Mouse acceleration kicks downward
                state.PosVelY -= s.PosForceFactor * mouseAccel * dt;
                state.PosDispY += state.PosVelY * dt;
                state.PosDispY = Mathf.Clamp(
                    state.PosDispY, -s.MaxPosDisplacement, s.MaxPosDisplacement
                );

                // Apply on top of cached original values (not current bone state)
                state.Handle.SetRotation(state.OriginalRotation + state.RotDisplacement);
                state.Handle.SetY(state.OriginalY + state.PosDispY);
            }
        }

        public void Clear()
        {
            foreach (var state in states.Values)
            {
                state.RotVelocity = 0;
                state.RotDisplacement = 0;
                state.PosVelY = 0;
                state.PosDispY = 0;
                state.BoneSet = false;
            }
            initialized = false;
        }

        public void TriggerImpulse(float strengthX, float strengthY)
        {
            if (!initialized) return;
            foreach (var s in boneSettings)
            {
                if (!states.TryGetValue(s.BoneName, out var state)) continue;
                state.RotVelocity += s.ForceFactor * strengthX * 1f;
                state.PosVelY -= s.PosForceFactor * strengthY * 1f;
            }
        }
    }
}
