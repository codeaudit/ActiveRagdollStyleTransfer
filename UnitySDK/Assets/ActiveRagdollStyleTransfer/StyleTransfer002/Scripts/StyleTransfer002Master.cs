﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MLAgents;

public class StyleTransfer002Master : MonoBehaviour {
	public float FixedDeltaTime = 0.005f;
	public bool visualizeAnimator = true;

	// general observations
	public List<Muscle002> Muscles;
	public List<BodyPart002> BodyParts;
	public float ObsPhase;
	public Vector3 ObsCenterOfMass;
	public Vector3 ObsVelocity;

	// model observations
	// i.e. model = difference between mocap and actual)
	// ideally we dont want to generate model at inference
	public float PositionDistance;
	public float EndEffectorDistance; // feet, hands, head
	public float FeetRotationDistance; 
	public float RotationDistance;
	public float VelocityDistance;
	public float CenterOfMassDistance;
	public float SensorDistance;


	// debug variables
	public bool IgnorRewardUntilObservation;
	public float ErrorCutoff;
	public bool DebugShowWithOffset;
	public bool DebugMode;
	public bool DebugDisableMotor;
    [Range(-100,100)]
	public int DebugAnimOffset;


	public float TimeStep;
	public int AnimationIndex;
	public int EpisodeAnimationIndex;
	public int StartAnimationIndex;
	public bool UseRandomIndexForTraining;
	public bool UseRandomIndexForInference;
	public bool CameraFollowMe;
	public Transform CameraTarget;

	private bool _isDone;
	bool _resetCenterOfMassOnLastUpdate;
	bool _fakeVelocity;


	// public List<float> vector;

	private StyleTransfer002Animator _muscleAnimator;
	private StyleTransfer002Agent _agent;
	private Brain _brain;
	public bool IsInferenceMode;
	bool _phaseIsRunning;
	Random _random = new Random();
	Vector3 _lastCenterOfMass;

	// Use this for initialization
	void Start () {
		Time.fixedDeltaTime = FixedDeltaTime;

		BodyParts = new List<BodyPart002> ();
		BodyPart002 root = null;
		foreach (var t in GetComponentsInChildren<Transform>())
		{
			if (BodyHelper002.GetBodyPartGroup(t.name) == BodyHelper002.BodyPartGroup.None)
				continue;
			
			var bodyPart = new BodyPart002{
				Rigidbody = t.GetComponent<Rigidbody>(),
				Transform = t,
				Name = t.name,
				Group = BodyHelper002.GetBodyPartGroup(t.name), 
			};
			if (bodyPart.Group == BodyHelper002.BodyPartGroup.Hips)
				root = bodyPart;
			bodyPart.Root = root;
			bodyPart.Init();
			BodyParts.Add(bodyPart);
		}
		var partCount = BodyParts.Count;

		Muscles = new List<Muscle002> ();
		var muscles = GetComponentsInChildren<ConfigurableJoint>();
		ConfigurableJoint rootConfigurableJoint = null;
		var ragDoll = GetComponent<RagDoll002>();
		foreach (var m in muscles)
		{
			var maximumForce = new Vector3(ragDoll.MusclePowers.First(x=>x.Muscle == m.name).Power,0,0);
			// maximumForce *= 2f;
			var muscle = new Muscle002{
				Rigidbody = m.GetComponent<Rigidbody>(),
				Transform = m.GetComponent<Transform>(),
				ConfigurableJoint = m,
				Name = m.name,
				Group = BodyHelper002.GetMuscleGroup(m.name),
				MaximumForce = maximumForce
			};
			if (muscle.Group == BodyHelper002.MuscleGroup.Hips)
				rootConfigurableJoint = muscle.ConfigurableJoint;
			muscle.RootConfigurableJoint = rootConfigurableJoint;
			muscle.Init();

			Muscles.Add(muscle);			
		}
		_muscleAnimator = FindObjectOfType<StyleTransfer002Animator>();
		_agent = FindObjectOfType<StyleTransfer002Agent>();
		_brain = FindObjectOfType<Brain>();
		switch (_brain.brainType)
		{
			case BrainType.External:
				IsInferenceMode = false;
				break;
			case BrainType.Player:
			case BrainType.Internal:
			case BrainType.Heuristic:
				IsInferenceMode = true;
				break;
			default:
				throw new System.NotImplementedException();
		}
	}
	
	// Update is called once per frame
	void Update () {
	}
	static float SumAbs(Vector3 vector)
	{
		var sum = Mathf.Abs(vector.x);
		sum += Mathf.Abs(vector.y);
		sum += Mathf.Abs(vector.z);
		return sum;
	}
	static float SumAbs(Quaternion q)
	{
		var sum = Mathf.Abs(q.w);
		sum += Mathf.Abs(q.x);
		sum += Mathf.Abs(q.y);
		sum += Mathf.Abs(q.z);
		return sum;
	}

	void FixedUpdate()
	{
		if (DebugMode)
			AnimationIndex = 0;
		var debugStepIdx = AnimationIndex;
		StyleTransfer002Animator.AnimationStep animStep = null;
		StyleTransfer002Animator.AnimationStep debugAnimStep = null;
		if (_phaseIsRunning) {
				debugStepIdx += DebugAnimOffset;
			if (DebugShowWithOffset){
				debugStepIdx = Mathf.Clamp(debugStepIdx, 0, _muscleAnimator.AnimationSteps.Count);
				debugAnimStep = _muscleAnimator.AnimationSteps[debugStepIdx];
			}
			animStep = _muscleAnimator.AnimationSteps[AnimationIndex];
		}
		PositionDistance = 0f;
		EndEffectorDistance = 0f;
		FeetRotationDistance = 0f;
		RotationDistance = 0f;
		VelocityDistance = 0f;
		CenterOfMassDistance = 0f;
		SensorDistance = 0f;
		if (_phaseIsRunning && DebugShowWithOffset)
			MimicAnimationFrame(debugAnimStep);
		else if (_phaseIsRunning)
			CompareAnimationFrame(animStep);
		foreach (var muscle in Muscles)
		{
			var i = Muscles.IndexOf(muscle);
			muscle.UpdateObservations();
			if (!DebugShowWithOffset && !DebugDisableMotor)
				muscle.UpdateMotor();
			if (!muscle.Rigidbody.useGravity)
				continue; // skip sub joints
		}
		foreach (var bodyPart in BodyParts)
		{
			if (_phaseIsRunning){
				bodyPart.UpdateObservations();
				PositionDistance += bodyPart.ObsDeltaFromAnimationPosition.sqrMagnitude;
				// PositionDistance += bodyPart.ObsDeltaFromAnimationPosition.magnitude;
				
				var rotDistance = bodyPart.ObsAngleDeltaFromAnimationRotation;
				var squareRotDistance = Mathf.Pow(rotDistance,2);
				RotationDistance += squareRotDistance;
				// RotationDistance += rotDistance;
				if (bodyPart.Group == BodyHelper002.BodyPartGroup.Hand
					|| bodyPart.Group == BodyHelper002.BodyPartGroup.Torso
					|| bodyPart.Group == BodyHelper002.BodyPartGroup.Foot)
				{
					EndEffectorDistance += bodyPart.ObsDeltaFromAnimationPosition.sqrMagnitude;
					// EndEffectorDistance += bodyPart.ObsDeltaFromAnimationPosition.magnitude;
				}
				if (bodyPart.Group == BodyHelper002.BodyPartGroup.Foot)
				{
					FeetRotationDistance += squareRotDistance;
					// EndEffectorRotDistance += rotDistance;
				}
			}
		}

		// RotationDistance *= RotationDistance; // take the square;
		ObsCenterOfMass = GetCenterOfMass();
		if (_phaseIsRunning)
			CenterOfMassDistance = (animStep.CenterOfMass - ObsCenterOfMass).sqrMagnitude;
		ObsVelocity = ObsCenterOfMass-_lastCenterOfMass;
		if (_fakeVelocity)
			ObsVelocity = animStep.Velocity;
		_lastCenterOfMass = ObsCenterOfMass;
		if (!_resetCenterOfMassOnLastUpdate)
			_fakeVelocity = false;

		if (_phaseIsRunning){
			var animVelocity = animStep.Velocity / Time.fixedDeltaTime;
			ObsVelocity /= Time.fixedDeltaTime;
			var velocityDistance = ObsVelocity-animVelocity;
			VelocityDistance = velocityDistance.sqrMagnitude;
			var sensorDistance = 0.0;
			var sensorDistanceStep = 1.0 / _agent.SensorIsInTouch.Count;
			for (int i = 0; i < _agent.SensorIsInTouch.Count; i++)
			{
				if (animStep.SensorIsInTouch[i] != _agent.SensorIsInTouch[i])
					sensorDistance += sensorDistanceStep;
			}
			SensorDistance = (float) sensorDistance;
		}
		// normalize distances
		// VelocityDistance /= 10f;
		VelocityDistance = Mathf.Clamp(VelocityDistance, -1f, 1f);
		// CenterOfMassDistance

		if (IgnorRewardUntilObservation)
			IgnorRewardUntilObservation = false;

		if (_phaseIsRunning){
			if (!DebugShowWithOffset)
				AnimationIndex++;
			if (AnimationIndex>=_muscleAnimator.AnimationSteps.Count) {
				//ResetPhase();
				Done();
				AnimationIndex--;
			}
			ObsPhase = _muscleAnimator.AnimationSteps[AnimationIndex].NormalizedTime % 1f;
		}
		if (_phaseIsRunning && IsInferenceMode && CameraFollowMe)
		{
			_muscleAnimator.anim.enabled = true;
			_muscleAnimator.anim.Play("Record",0, animStep.NormalizedTime);
			_muscleAnimator.anim.transform.position = animStep.TransformPosition;
			_muscleAnimator.anim.transform.rotation = animStep.TransformRotation;
		}

	}
	void CompareAnimationFrame(StyleTransfer002Animator.AnimationStep animStep)
	{
		MimicAnimationFrame(animStep, true);
	}

	void MimicAnimationFrame(StyleTransfer002Animator.AnimationStep animStep, bool onlySetAnimation = false)
	{
		foreach (var bodyPart in BodyParts)
		{
			var i = animStep.Names.IndexOf(bodyPart.Name);
			Vector3 animPosition = bodyPart.InitialRootPosition + animStep.Positions[0];
            Quaternion animRotation = bodyPart.InitialRootRotation * animStep.Rotaions[0];
			if (i != 0) {
				animPosition += animStep.Positions[i];
				animRotation = bodyPart.InitialRootRotation * animStep.Rotaions[i];
			}
			Vector3 angularVelocity = animStep.AngularVelocities[i] / Time.fixedDeltaTime;
			Vector3 velocity = animStep.Velocities[i] / Time.fixedDeltaTime;
			if (!onlySetAnimation)
				bodyPart.MoveToAnim(animPosition, animRotation, angularVelocity, velocity);
			bodyPart.SetAnimationPosition(animStep.Positions[i], animStep.Rotaions[i]);
		}
	}

	protected virtual void LateUpdate() {
		if (_resetCenterOfMassOnLastUpdate){
			ObsCenterOfMass = GetCenterOfMass();
			_lastCenterOfMass = ObsCenterOfMass;
			_resetCenterOfMassOnLastUpdate = false;
		}
		#if UNITY_EDITOR
			VisualizeTargetPose();
		#endif
	}

	public bool IsDone()
	{
		return _isDone;
	}
	void Done()
	{
		_isDone = true;
	}

	public void ResetPhase()
	{
		// _animationIndex =  UnityEngine.Random.Range(0, _muscleAnimator.AnimationSteps.Count);
		if (!_phaseIsRunning){
			StartAnimationIndex = _muscleAnimator.AnimationSteps.Count-1;
			EpisodeAnimationIndex = _muscleAnimator.AnimationSteps.Count-1;
			AnimationIndex = EpisodeAnimationIndex;
			if (CameraFollowMe){
				var camera = FindObjectOfType<Camera>();
				var follow = camera.GetComponent<SmoothFollow>();
				follow.target = CameraTarget;
			}
		}
		// ErrorCutoff = UnityEngine.Random.Range(-15f, 2f);
		// ErrorCutoff = UnityEngine.Random.Range(-10f, 1f);
		// ErrorCutoff = UnityEngine.Random.Range(-5f, 1f);
		ErrorCutoff = UnityEngine.Random.Range(-3f, .5f);
		if (IsInferenceMode)
			ErrorCutoff = UnityEngine.Random.Range(-3f, -3f);
		var lastLenght = AnimationIndex - EpisodeAnimationIndex;
		if (lastLenght >=  _muscleAnimator.AnimationSteps.Count-2){
			StartAnimationIndex = _muscleAnimator.AnimationSteps.Count-1;
			// StartAnimationIndex = 0;
			// ErrorCutoff += 0.25f;
			EpisodeAnimationIndex = _muscleAnimator.AnimationSteps.Count-1;
			AnimationIndex = EpisodeAnimationIndex;
		}

		// start with random
		AnimationIndex = UnityEngine.Random.Range(0, _muscleAnimator.AnimationSteps.Count);
		if (IsInferenceMode && !UseRandomIndexForInference){
			AnimationIndex = 1;
		} else if (!IsInferenceMode && !UseRandomIndexForTraining) {
			var minIdx = StartAnimationIndex;
			if (_muscleAnimator.IsLoopingAnimation)
				minIdx = minIdx == 0 ? 1 : minIdx;
			var maxIdx = _muscleAnimator.AnimationSteps.Count-1;
			var range = 30f;//maxIdx-minIdx;
			var rnd = (NextGaussian() /3f) * (float) range;
			var idx = Mathf.Clamp((float)minIdx + rnd, minIdx, (float)maxIdx);
			AnimationIndex = (int)idx;
		}
		// AnimationIndex = StartAnimationIndex;
		_phaseIsRunning = true;
		_isDone = false;
		var animStep = _muscleAnimator.AnimationSteps[AnimationIndex];
		TimeStep = animStep.TimeStep;
		PositionDistance = 0f;
		EndEffectorDistance = 0f;
		FeetRotationDistance = 0f;
		RotationDistance = 0f;
		VelocityDistance = 0f;
		IgnorRewardUntilObservation = true;
		_resetCenterOfMassOnLastUpdate = true;
		_fakeVelocity = true;
		foreach (var muscle in Muscles)
			muscle.Init();
		foreach (var bodyPart in BodyParts)
			bodyPart.Init();
		MimicAnimationFrame(animStep);
		EpisodeAnimationIndex = AnimationIndex;
	}

	Vector3 GetCenterOfMass()
	{
		var centerOfMass = Vector3.zero;
		float totalMass = 0f;
		var bodies = BodyParts
			.Select(x=>x.Rigidbody)
			.Where(x=>x!=null)
			.ToList();
		foreach (Rigidbody rb in bodies)
		{
			centerOfMass += rb.worldCenterOfMass * rb.mass;
			totalMass += rb.mass;
		}
		centerOfMass /= totalMass;
		centerOfMass -= transform.parent.position;
		return centerOfMass;
	}

	float NextGaussian(float mu = 0, float sigma = 1)
	{
		var u1 = UnityEngine.Random.value;
		var u2 = UnityEngine.Random.value;

		var rand_std_normal = Mathf.Sqrt(-2.0f * Mathf.Log(u1)) *
							Mathf.Sin(2.0f * Mathf.PI * u2);

		var rand_normal = mu + sigma * rand_std_normal;

		return rand_normal;
	}

	private void VisualizeTargetPose() {
		if (!visualizeAnimator) return;
		if (!Application.isEditor) return;

		// foreach (Muscle002 m in Muscles) {
		// 	if (m.ConfigurableJoint.connectedBody != null && m.connectedBodyTarget != null) {
		// 		Debug.DrawLine(m.target.position, m.connectedBodyTarget.position, Color.cyan);
				
		// 		bool isEndMuscle = true;
		// 		foreach (Muscle002 m2 in Muscles) {
		// 			if (m != m2 && m2.ConfigurableJoint.connectedBody == m.rigidbody) {
		// 				isEndMuscle = false;
		// 				break;
		// 			}
		// 		}
				
		// 		if (isEndMuscle) VisualizeHierarchy(m.target, Color.cyan);
		// 	}
		// }
	}
	
	// Recursively visualizes a bone hierarchy
	private void VisualizeHierarchy(Transform t, Color color) {
		for (int i = 0; i < t.childCount; i++) {
			Debug.DrawLine(t.position, t.GetChild(i).position, color);
			VisualizeHierarchy(t.GetChild(i), color);
		}
	}


}