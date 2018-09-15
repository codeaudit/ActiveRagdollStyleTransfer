﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MLAgents;
using System.Linq;

public class StyleTransfer002Agent : Agent, IOnSensorCollision {

	public List<float> SensorIsInTouch;
	StyleTransfer002Master _master;
	StyleTransfer002Animator _styleAnimator;

	List<GameObject> _sensors;

	public bool ShowMonitor = false;

	// Use this for initialization
	void Start () {
		_master = GetComponent<StyleTransfer002Master>();
		_styleAnimator = FindObjectOfType<StyleTransfer002Animator>();
	}
	
	// Update is called once per frame
	void Update () {
		if (agentParameters.onDemandDecision && _styleAnimator.AnimationStepsReady){
			agentParameters.onDemandDecision = false;
			_master.ResetPhase();
		}
	}

	override public void InitializeAgent()
	{

	}

	override public void CollectObservations()
	{
		// AddVectorObs(_master.PositionDistance);
		// AddVectorObs(_master.RotationDistance);
		// AddVectorObs(_master.TotalDistance);
		AddVectorObs(_master.Phase);

		foreach (var bodyPart in _master.BodyParts)
		{
			// if (bodyPart.Transform.GetComponent<ConfigurableJoint>() == null)
			// 	continue;
			AddVectorObs(bodyPart.ObsLocalPosition);
			AddVectorObs(bodyPart.ObsRotation);
			AddVectorObs(bodyPart.ObsRotationVelocity);
			AddVectorObs(bodyPart.ObsVelocity);
		}
		foreach (var muscle in _master.Muscles)
		{
			if (muscle.ConfigurableJoint.angularXMotion != ConfigurableJointMotion.Locked)
				AddVectorObs(muscle.TargetNormalizedRotationX);
			if (muscle.ConfigurableJoint.angularYMotion != ConfigurableJointMotion.Locked)
				AddVectorObs(muscle.TargetNormalizedRotationY);
			if (muscle.ConfigurableJoint.angularZMotion != ConfigurableJointMotion.Locked)
				AddVectorObs(muscle.TargetNormalizedRotationZ);
		}

		if (SensorIsInTouch?.Count>0){
			AddVectorObs(SensorIsInTouch[0]);
			AddVectorObs(0f);
			AddVectorObs(SensorIsInTouch[1]);
			AddVectorObs(0f);
		}
	}

	public override void AgentAction(float[] vectorAction, string textAction)
	{
		int i = 0;
		foreach (var muscle in _master.Muscles)
		{
			// if(muscle.Parent == null)
			// 	continue;
			if (muscle.ConfigurableJoint.angularXMotion != ConfigurableJointMotion.Locked)
				muscle.TargetNormalizedRotationX = vectorAction[i++];
			if (muscle.ConfigurableJoint.angularYMotion != ConfigurableJointMotion.Locked)
				muscle.TargetNormalizedRotationY = vectorAction[i++];
			if (muscle.ConfigurableJoint.angularZMotion != ConfigurableJointMotion.Locked)
				muscle.TargetNormalizedRotationZ = vectorAction[i++];
		}
        var jointsAtLimitPenality = GetJointsAtLimitPenality() * 4;
        float effort = GetEffort();
        var effortPenality = 0.05f * (float)effort;
		
		var poseReward = 1f - _master.RotationDistance;
		var velocityReward = 1f - Mathf.Abs(_master.VelocityDistance);
		var endEffectorReward = 1f - _master.EndEffectorDistance;
		var centerMassReward = 1f - _master.CenterOfMassDistance; // TODO

		float poseRewardScale = .65f;
		// poseRewardScale *=2;
		float velocityRewardScale = .1f;
		float endEffectorRewardScale = .15f;
		float centerMassRewardScale = .1f;

		poseReward = Mathf.Clamp(poseReward, 0f, 1f);
		velocityReward = Mathf.Clamp(velocityReward, 0f, 1f);
		endEffectorReward = Mathf.Clamp(endEffectorReward, 0f, 1f);
		centerMassReward = Mathf.Clamp(centerMassReward, 0f, 1f);

		int terminateSignals = 0;
		terminateSignals += poseReward <= 0f ? 1 : 0;
		terminateSignals += velocityReward <= 0f ? 1 : 0;
		terminateSignals += endEffectorRewardScale <= 0f ? 1 : 0;
		terminateSignals += centerMassReward <= 0f ? 1 : 0;
		bool shouldTerminate = terminateSignals >= 2 ? true : false;

		float distanceReward = 
			(poseReward * poseRewardScale) +
			(velocityReward * velocityRewardScale) +
			(endEffectorReward * endEffectorRewardScale) +
			(centerMassReward * centerMassRewardScale);
		float reward = 
			distanceReward
			// - effortPenality +
			- jointsAtLimitPenality;

        if (ShowMonitor) {
            var hist = new []{
                reward,
				distanceReward,
                - jointsAtLimitPenality, 
                // - effortPenality, 
				(poseReward * poseRewardScale),
				(velocityReward * velocityRewardScale),
				(endEffectorReward * endEffectorRewardScale),
				(centerMassReward * centerMassRewardScale),
				}.ToList();
            Monitor.Log("rewardHist", hist.ToArray());
        }

		if (!_master.IgnorRewardUntilObservation)
			AddReward(reward);
		if (!IsDone()){
			// if (distanceReward < _master.ErrorCutoff && !_master.DebugShowWithOffset) {
			if (shouldTerminate && !_master.DebugShowWithOffset) {
				AddReward(-1f);
				Done();
				// _master.StartAnimationIndex = _muscleAnimator.AnimationSteps.Count-1;
				if (_master.StartAnimationIndex < _styleAnimator.AnimationSteps.Count-1)
					_master.StartAnimationIndex++;
			}
			if (_master.IsDone()){
				// AddReward(1f*(float)this.GetStepCount());
				Done();
				// if (_master.StartAnimationIndex > 0 && distanceReward >= _master.ErrorCutoff)
				if (_master.StartAnimationIndex > 0 && !shouldTerminate)
				 	_master.StartAnimationIndex--;
			}
		}
	}
	float GetEffort(string[] ignorJoints = null)
	{
		double effort = 0;
		foreach (var muscle in _master.Muscles)
		{
			if(muscle.Parent == null)
				continue;
			var name = muscle.Name;
			if (ignorJoints != null && ignorJoints.Contains(name))
				continue;
			var jointEffort = Mathf.Pow(Mathf.Abs(muscle.TargetNormalizedRotationX),2);
			effort += jointEffort;
			jointEffort = Mathf.Pow(Mathf.Abs(muscle.TargetNormalizedRotationY),2);
			effort += jointEffort;
			jointEffort = Mathf.Pow(Mathf.Abs(muscle.TargetNormalizedRotationZ),2);
			effort += jointEffort;
		}
		return (float)effort;
	}	
	float GetJointsAtLimitPenality(string[] ignorJoints = null)
	{
		int atLimitCount = 0;
		foreach (var muscle in _master.Muscles)
		{
			if(muscle.Parent == null)
				continue;

			var name = muscle.Name;
			if (ignorJoints != null && ignorJoints.Contains(name))
				continue;
			if (Mathf.Abs(muscle.TargetNormalizedRotationX) >= 1f)
				atLimitCount++;
			if (Mathf.Abs(muscle.TargetNormalizedRotationY) >= 1f)
				atLimitCount++;
			if (Mathf.Abs(muscle.TargetNormalizedRotationZ) >= 1f)
				atLimitCount++;
            }
            float penality = atLimitCount * 0.2f;
            return (float)penality;
        }

	// public override void AgentOnDone()
	// {

	// }

	public override void AgentReset()
	{
		_sensors = _master.BodyParts
			.Where(x=>x.Group == BodyHelper002.BodyPartGroup.Foot)
			.Select(x=>x.Rigidbody.gameObject)
			.ToList();
		foreach (var sensor in _sensors)
		{
			if (sensor.GetComponent<SensorBehavior>()== null)
				sensor.AddComponent<SensorBehavior>();
		}
		SensorIsInTouch = Enumerable.Range(0,_sensors.Count).Select(x=>0f).ToList();
		if (!agentParameters.onDemandDecision)
			_master.ResetPhase();
	}

	public void OnSensorCollisionEnter(Collider sensorCollider, Collision other) {
			if (string.Compare(other.gameObject.name, "Terrain", true) !=0)
                return;
			var otherGameobject = other.gameObject;
            var sensor = _sensors
                .FirstOrDefault(x=>x == sensorCollider.gameObject);
            if (sensor != null) {
                var idx = _sensors.IndexOf(sensor);
                SensorIsInTouch[idx] = 1f;
            }
		}
        public void OnSensorCollisionExit(Collider sensorCollider, Collision other)
        {
            if (string.Compare(other.gameObject.name, "Terrain", true) !=0)
                return;
			var otherGameobject = other.gameObject;
            var sensor = _sensors
                .FirstOrDefault(x=>x == sensorCollider.gameObject);
            if (sensor != null) {
                var idx = _sensors.IndexOf(sensor);
                SensorIsInTouch[idx] = 0f;
            }
        }  

}
