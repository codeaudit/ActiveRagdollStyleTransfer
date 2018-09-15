﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class StyleTransfer002Animator : MonoBehaviour {

	internal Animator anim;

	public List<AnimationStep> AnimationSteps;
	public bool AnimationStepsReady;
	public bool IsLoopingAnimation;

	[Range(0f,1f)]
	public float NormalizedTime;
	public float Lenght;
	// public float CurTime;

	// StyleTransfer002Master _master;
	private List<Vector3> _lastPosition;
	private List<Quaternion> _lastRotation;
	// public string animName = "0008_Skipping002";
	private bool _isRagDoll;

	Quaternion _initialBaseRotation;
	List<Quaternion> _initialRotations;

	public List<BodyPart002> BodyParts;

    private Vector3 _lastVelocityPosition;

    [System.Serializable]
	public class AnimationStep
	{
		public float TimeStep;
		public float NormalizedTime;
		// public List<Vector3> RootPositions;
		public List<Vector3> Velocities;
		public Vector3 Velocity;
		public List<Quaternion> RotaionVelocities;
		public List<Vector3> AngularVelocities;
		// public List<Vector3> NormalizedAngularVelocities;
		// public List<Quaternion> RootRotations;
		public List<Vector3> RootAngles;

		public List<Vector3> Positions;
		public List<Quaternion> Rotaions;
		public List<string> Names;
		public Vector3 CenterOfMass;
	}

	// Use this for initialization
	void Start () {
		anim = GetComponent<Animator>();
		anim.Play("Record",0, NormalizedTime);
		anim.Update(0f);
		// _master = FindObjectOfType<StyleTransfer002Master>();
		AnimationSteps = new List<AnimationStep>();
		_initialBaseRotation = transform.rotation;
	}
	void Reset()
	{
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

		_lastPosition = Enumerable.Repeat(Vector3.zero, partCount).ToList();
		_lastRotation = Enumerable.Repeat(Quaternion.identity, partCount).ToList();
		_lastVelocityPosition = transform.position;
		var anims = GetComponentsInChildren<Transform>();
		//_bodyParts = _master.Muscles.Select(x=> anims.First(y=>y.name == x.Name).transform).ToList();
		_initialRotations = BodyParts
			.Select(x=> x.Transform.rotation)
			.ToList();
		BecomeAnimated();
	}
	
	void FixedUpdate () {
		if (_lastPosition == null)
			Reset();
		AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
		AnimatorClipInfo[] clipInfo = anim.GetCurrentAnimatorClipInfo(0);
		Lenght = stateInfo.length;
		NormalizedTime = stateInfo.normalizedTime;
		IsLoopingAnimation = stateInfo.loop;
		var timeStep = stateInfo.length * stateInfo.normalizedTime;
		var endTime = 1f;
		if (IsLoopingAnimation)
			endTime = 3f;
		if (NormalizedTime <= endTime) {
			MimicAnimation();
			UpdateAnimationStep(timeStep);
		}
		else {
			StopAnimation();
			// BecomeRagDoll();
		}
	}
	void UpdateAnimationStep(float timeStep)
    {
		// HACK deal with two of first frame
		if (NormalizedTime == 0f && AnimationSteps.FirstOrDefault(x=>x.NormalizedTime == 0f) != null)
			return;

		// var c = _master.Muscles.Count;
		var c = BodyParts.Count;
		var animStep = new AnimationStep();
		animStep.TimeStep = timeStep;
		animStep.NormalizedTime = NormalizedTime;
		// animStep.RootPositions = Enumerable.Repeat(Vector3.zero, c).ToList();
		animStep.Velocities = Enumerable.Repeat(Vector3.zero, c).ToList();
		animStep.RotaionVelocities = Enumerable.Repeat(Quaternion.identity, c).ToList();
		animStep.AngularVelocities = Enumerable.Repeat(Vector3.zero, c).ToList();
		// animStep.NormalizedAngularVelocities = Enumerable.Repeat(Vector3.zero, c).ToList();
		// animStep.RootRotations = Enumerable.Repeat(Quaternion.identity, c).ToList();
		animStep.RootAngles = Enumerable.Repeat(Vector3.zero, c).ToList();
		animStep.Positions = Enumerable.Repeat(Vector3.zero, c).ToList();
		animStep.Rotaions = Enumerable.Repeat(Quaternion.identity, c).ToList();
		animStep.Velocity = transform.position - _lastVelocityPosition;
		animStep.Names = BodyParts.Select(x=>x.Name).ToList();
		_lastVelocityPosition = transform.position;

		var rootBone = BodyParts[0];

		foreach (var bodyPart in BodyParts)
		{
			var i = BodyParts.IndexOf(bodyPart);
			if (i ==0) {
				animStep.Rotaions[i] = Quaternion.Inverse(bodyPart.InitialRootRotation) * bodyPart.Transform.rotation;
				animStep.Positions[i] =  bodyPart.Transform.position - bodyPart.InitialRootPosition;
				animStep.RootAngles[i] = animStep.Rotaions[i].eulerAngles;
			}
			else {
				animStep.Rotaions[i] = Quaternion.Inverse(rootBone.Transform.rotation) * bodyPart.Transform.rotation;
				animStep.RootAngles[i] = animStep.Rotaions[i].eulerAngles;
				animStep.Positions[i] =  bodyPart.Transform.position - rootBone.Transform.position;
			}
			
			if (NormalizedTime != 0f) {
				animStep.Velocities[i] = bodyPart.Transform.position - _lastPosition[i];
				animStep.RotaionVelocities[i] = JointHelper002.FromToRotation(_lastRotation[i], bodyPart.Transform.rotation);
				animStep.AngularVelocities[i] = animStep.RotaionVelocities[i].eulerAngles;
			}
			_lastPosition[i] = bodyPart.Transform.position;
			_lastRotation[i] = bodyPart.Transform.rotation;

		}
		animStep.CenterOfMass = GetCenterOfMass();
		AnimationSteps.Add(animStep);
    }
	public void BecomeAnimated()
	{
		var rigidbodies = GetComponentsInChildren<Rigidbody>().ToList();
		foreach (var rb in rigidbodies)
		{
			rb.isKinematic = true;
		}
		_isRagDoll = false;
	}
	public void BecomeRagDoll()
	{
		var rigidbodies = GetComponentsInChildren<Rigidbody>().ToList();
		foreach (var rb in rigidbodies)
		{
			rb.isKinematic = false;
			// rb.angularVelocity = Vector3.zero;
			// rb.velocity = Vector3.zero;
		}
		_isRagDoll = true;
	}
	public void StopAnimation()
	{
		AnimationStepsReady = true;
		anim.enabled=false;
	}
	protected virtual void LateUpdate() {
		MimicAnimation();
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
	public void MimicAnimation()
	{
		if (!anim.enabled)
			return;

        // MimicBone("butt", "mixamorig:Hips", new Vector3(.01f, -.057f, .004f), Quaternion.Euler(90, 88.2f, 88.8f));
        // MimicBone("butt", 			"mixamorig:Hips", 			new Vector3(.0f, .0f, .0f), 			Quaternion.Euler(90, 88.2f, 88.8f));
        MimicBone("butt", 			"mixamorig:Hips", 			new Vector3(.0f, -.055f, .0f), 			Quaternion.Euler(90, 0f, 0f));
        MimicBone("lower_waist",    "mixamorig:Spine",          new Vector3(.0f, .0153f, .0f), 			Quaternion.Euler(90, 0f, 0f));
        // MimicBone("upper_waist",    "mixamorig:Spine1",         new Vector3(.0f, .0465f, .0f), 			Quaternion.Euler(90, 0f, 0f));
        MimicBone("torso",          "mixamorig:Spine2",         new Vector3(.0f, .04f, .0f), 			Quaternion.Euler(90, 0f, 0f));
		//Quaternion.Euler(90, -90f, 180f));
        // MimicBone("head",           "mixamorig:Head",           new Vector3(.0f, .05f, .0f), 			Quaternion.Euler(0, 0f, 0f));

        MimicBone("left_upper_arm",   "mixamorig:LeftArm", "mixamorig:LeftForeArm", new Vector3(.0f, .0f, .0f), Quaternion.Euler(0, 45, 180));
        MimicBone("left_larm",        "mixamorig:LeftForeArm",  "mixamorig:LeftHand", new Vector3(.0f, .0f, .0f), Quaternion.Euler(0, -180-45, 180));
        //MimicBone("left_hand",        "mixamorig:LeftHand", new Vector3(.0f, .0f, .0f), 			Quaternion.Euler(0, 90, 90+180));
        
        MimicBone("right_upper_arm",  "mixamorig:RightArm", "mixamorig:RightForeArm",      new Vector3(.0f, .0f, .0f), Quaternion.Euler(0, 180-45, 180));
        MimicBone("right_larm",       "mixamorig:RightForeArm", "mixamorig:RightHand",  new Vector3(.0f, .0f, .0f), Quaternion.Euler(0, 90-45, 180));
        // MimicBone("right_hand",       "mixamorig:RightHand",      new Vector3(.0f, .0f, .0f), Quaternion.Euler(0, 180-90, -90));

        MimicBone("left_thigh",       "mixamorig:LeftUpLeg",  "mixamorig:LeftLeg",    new Vector3(.0f, .0f, .0f), 			Quaternion.Euler(0, 0, 180));
        MimicBone("left_shin",        "mixamorig:LeftLeg",    "mixamorig:LeftFoot",   new Vector3(.0f, .02f, .0f), 			Quaternion.Euler(0, 0, 180));
        // MimicBone("left_left_foot",   "mixamorig:LeftToeBase",    new Vector3(.024f, .044f, -.06f), 			Quaternion.Euler(3, -90, 180));//3));
        // MimicBone("right_left_foot",  "mixamorig:LeftToeBase",    new Vector3(-.024f, .044f, -.06f),  			Quaternion.Euler(-8, -90, 180));//-8));
        // MimicLeftFoot("left_left_foot",   new Vector3(.024f, -.01215f, -.06f), 			Quaternion.Euler(3, -90, 180));//3));
        // MimicLeftFoot("right_left_foot",  new Vector3(-.024f, -.01215f, -.06f),  			Quaternion.Euler(-8, -90, 180));//-8));
        // MimicLeftFoot("left_left_foot",   new Vector3(-.024f, -.01215f, -.06f), 			Quaternion.Euler(-8, -90, 180));//3));

        MimicBone("right_thigh",      "mixamorig:RightUpLeg", "mixamorig:RightLeg", new Vector3(.0f, .0f, .0f), 			Quaternion.Euler(0, 0, 180));
        MimicBone("right_shin",       "mixamorig:RightLeg",   "mixamorig:RightFoot", new Vector3(.0f, .02f, .0f), 			Quaternion.Euler(0, 0, 180));
        // MimicBone("right_right_foot", "mixamorig:RightToeBase",   new Vector3(.024f, .044f, -.06f),  			Quaternion.Euler(3, -90, 180));//3));
        // MimicBone("left_right_foot",  "mixamorig:RightToeBase",   new Vector3(-.024f, .044f, -.06f), 		Quaternion.Euler(-8, -90, 180));//-8));
        // MimicRightFoot("right_right_foot", new Vector3(.024f, .044f, -.06f),  			Quaternion.Euler(3, -90, 180));//3));
        // MimicRightFoot("left_right_foot",  new Vector3(-.024f, .044f, -.06f), 		Quaternion.Euler(-8, -90, 180));//-8));
        // MimicRightFoot("right_right_foot", new Vector3(.024f, -.01215f, -.06f),  			Quaternion.Euler(3, -90, 180));//3));
        // MimicRightFoot("left_right_foot",  new Vector3(-.024f, -.01215f, -.06f), 		Quaternion.Euler(-8, -90, 180));//-8));
        // MimicRightFoot("right_right_foot", new Vector3(.024f, -.01215f, -.06f),  			Quaternion.Euler(3, -90, 180));//3));
        // MimicRightFoot("right_right_foot", new Vector3(.0243f, -.0f, -.0243f),  			Quaternion.Euler(3, -90, 180));//3));
        // MimicLeftFoot("left_left_foot",   new Vector3(-.0243f, -.0f, -.0243f), 			Quaternion.Euler(-8, -90, 180));//3));
        MimicRightFoot("right_right_foot", new Vector3(.0f, -.0f, -.0f),  			Quaternion.Euler(3, -90, 180));//3));
        MimicLeftFoot("left_left_foot",   new Vector3(-.0f, -.0f, -.0f), 			Quaternion.Euler(-8, -90, 180));//3));
		

	}
	void MimicBone(string name, string bodyPartName, Vector3 offset, Quaternion rotationOffset)
	{
		var rigidbodies = GetComponentsInChildren<Rigidbody>().ToList();
		var transforms = GetComponentsInChildren<Transform>().ToList();

		var bodyPart = transforms.First(x=>x.name == bodyPartName);
		var target = rigidbodies.First(x=>x.name == name);

		target.transform.position = bodyPart.transform.position + offset;
		target.transform.rotation = bodyPart.transform.rotation * rotationOffset;
	}

	void MimicBone(string name, string animStartName, string animEndtName, Vector3 offset, Quaternion rotationOffset)
	{
		var rigidbodies = GetComponentsInChildren<Rigidbody>().ToList();
		var transforms = GetComponentsInChildren<Transform>().ToList();

		var animStartBone = transforms.First(x=>x.name == animStartName);
		var animEndBone = transforms.First(x=>x.name == animEndtName);
		var target = rigidbodies.First(x=>x.name == name);

		var pos = (animEndBone.transform.position - animStartBone.transform.position);
		target.transform.position = animStartBone.transform.position + (pos/2) + offset;
		target.transform.rotation = animStartBone.transform.rotation * rotationOffset;
	}
	[Range(0f,1f)]
	public float toePositionOffset = .3f;
	[Range(0f,1f)]
	public float toeRotationOffset = .7f;
	void MimicLeftFoot(string name, Vector3 offset, Quaternion rotationOffset)
	{
		string animStartName = "mixamorig:LeftFoot";
		// string animEndtName = "mixamorig:LeftToeBase";
		string animEndtName = "mixamorig:LeftToe_End";
		var rigidbodies = GetComponentsInChildren<Rigidbody>().ToList();
		var transforms = GetComponentsInChildren<Transform>().ToList();

		var animStartBone = transforms.First(x=>x.name == animStartName);
		var animEndBone = transforms.First(x=>x.name == animEndtName);
		var target = rigidbodies.First(x=>x.name == name);

		var rotation = Quaternion.Lerp(animStartBone.rotation, animEndBone.rotation, toeRotationOffset);
		var skinOffset = (animEndBone.transform.position - animStartBone.transform.position);
		target.transform.position = animStartBone.transform.position + (skinOffset * toePositionOffset) + offset;
		target.transform.rotation = rotation * rotationOffset;
	}
	void MimicRightFoot(string name, Vector3 offset, Quaternion rotationOffset)
	{
		string animStartName = "mixamorig:RightFoot";
		// string animEndtName = "mixamorig:RightToeBase";
		string animEndtName = "mixamorig:RightToe_End";
		var rigidbodies = GetComponentsInChildren<Rigidbody>().ToList();
		var transforms = GetComponentsInChildren<Transform>().ToList();

		var animStartBone = transforms.First(x=>x.name == animStartName);
		var animEndBone = transforms.First(x=>x.name == animEndtName);
		var target = rigidbodies.First(x=>x.name == name);

		var rotation = Quaternion.Lerp(animStartBone.rotation, animEndBone.rotation, toeRotationOffset);
		var skinOffset = (animEndBone.transform.position - animStartBone.transform.position);
		target.transform.position = animStartBone.transform.position + (skinOffset * toePositionOffset) + offset;
		target.transform.rotation = rotation * rotationOffset;

	}
}
