using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VF.Builder;
using VF.Builder.Exceptions;
using VF.Component;
using VF.Feature.Base;
using VF.Model;
using VRC.SDK3.Avatars.Components;

namespace VF.Feature {
    public class BakeGlobalCollidersBuilder : FeatureBuilder {
        [FeatureBuilderAction]
        public void Apply() {

            var fakeHead = allBuildersInRun.OfType<FakeHeadBuilder>().First();
            var globalContacts = avatarObject.GetComponentsInChildren<VRCFuryGlobalCollider>(true);
            var avatar = avatarObject.GetComponent<VRCAvatarDescriptor>();

            var fingers = new List<(HumanBodyBones, VRCAvatarDescriptor.ColliderConfig, Action<VRCAvatarDescriptor.ColliderConfig>)> {
                ( HumanBodyBones.LeftRingIntermediate, avatar.collider_fingerRingL, c => avatar.collider_fingerRingL = c ),
                ( HumanBodyBones.RightRingIntermediate, avatar.collider_fingerRingR, c => avatar.collider_fingerRingR = c ),
                ( HumanBodyBones.LeftLittleIntermediate, avatar.collider_fingerLittleL, c => avatar.collider_fingerLittleL = c ),
                ( HumanBodyBones.RightLittleIntermediate, avatar.collider_fingerLittleR, c => avatar.collider_fingerLittleR = c ),
                ( HumanBodyBones.LeftMiddleIntermediate, avatar.collider_fingerMiddleL, c => avatar.collider_fingerMiddleL = c ),
                ( HumanBodyBones.RightMiddleIntermediate, avatar.collider_fingerMiddleR, c => avatar.collider_fingerMiddleR = c ),
            };
            
            // Put unused fingers on the front of the list
            {
                var unused = new List<(HumanBodyBones, VRCAvatarDescriptor.ColliderConfig, Action<VRCAvatarDescriptor.ColliderConfig>)>();
                var used = new List<(HumanBodyBones, VRCAvatarDescriptor.ColliderConfig, Action<VRCAvatarDescriptor.ColliderConfig>)>();
                while (fingers.Count >= 2) {
                    var left = fingers[0];
                    var right = fingers[1];
                    fingers.RemoveRange(0, 2);
                    if (!IsFingerUsed(left.Item1, left.Item2) && (left.Item2.isMirrored || !IsFingerUsed(right.Item1, right.Item2))) {
                        unused.Add(left);
                        unused.Add(right);
                    } else {
                        used.Add(left);
                        used.Add(right);
                    }
                }
                fingers.Clear();
                fingers.AddRange(unused);
                fingers.AddRange(used);
            }
            
            if (globalContacts.Length > fingers.Count) {
                throw new VRCFBuilderException("Too many VRCF global colliders are present on this avatar");
            }

            var i = 0;
            foreach (var globalContact in globalContacts) {
                var target = globalContact.GetTransform();
                fakeHead.MarkEligible(target.gameObject);
                var finger = fingers[i].Item2;
                var setFinger = fingers[i].Item3;
                finger.isMirrored = false;
                finger.state = VRCAvatarDescriptor.ColliderConfig.State.Custom;
                finger.position = Vector3.zero;
                finger.radius = globalContact.radius;
                finger.rotation = Quaternion.identity;

                // Vrchat places the capsule for fingers in a very strange place, but essentially it will:
                // If collider length is 0, it will be a sphere centered on the set transform
                // If colllider length < radius*2, it will be a sphere in a weird in-between location
                // If collider length >= radius*2, it will be a capsule with one end attached to the set transform's parent,
                //   facing the direction of the set transform.
                
                var childObj = new GameObject("GlobalContact");
                childObj.transform.SetParent(target, false);
                if (globalContact.height <= globalContact.radius * 2) {
                    // It's a sphere
                    finger.transform = childObj.transform;
                    finger.height = 0;
                } else { 
                    // It's a capsule
                    childObj.transform.localPosition = new Vector3(0, 0, -globalContact.height / 2);
                    var directionObj = new GameObject("Direction");
                    directionObj.transform.SetParent(childObj.transform, false);
                    directionObj.transform.localPosition = new Vector3(0, 0, 0.0001f);
                    finger.transform = directionObj.transform;
                    finger.height = globalContact.height;
                }
                setFinger(finger);
                i++;
            }
            if (i % 2 == 1) {
                // If an odd number, disable the matching mirrored finger
                var finger = fingers[i].Item2;
                var setFinger = fingers[i].Item3;
                finger.isMirrored = false;
                finger.state = VRCAvatarDescriptor.ColliderConfig.State.Disabled;
                setFinger(finger);
            }
        }

        private bool IsFingerUsed(HumanBodyBones bone, VRCAvatarDescriptor.ColliderConfig config) {
            var animator = avatarObject.GetComponent<Animator>();
            if (config.state == VRCAvatarDescriptor.ColliderConfig.State.Disabled) return false;
            if (animator.GetBoneTransform(bone) == null) return false;
            return true;
        }
    }
}