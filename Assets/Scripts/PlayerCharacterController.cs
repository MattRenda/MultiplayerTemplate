using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class PlayerCharacterController : MonoBehaviour
{
	[Header("Movement Speeds")]
	public float walkSpeed = 4f;
	public float runSpeed = 7f;
	public float crouchSpeed = 2f;

	[Header("Jumping")]
	public float jumpVelocity = 5f;
	public float coyoteTime = 0.1f;

	[Header("Input Keys (Legacy KeyCode)")]
	public KeyCode crouchKey = KeyCode.LeftControl;
	public float crouchHeightMultiplier = 0.5f;
	public KeyCode runKey = KeyCode.LeftShift;
	public KeyCode jumpKey = KeyCode.Space;

	[Header("Ground Check")]
	public LayerMask groundMask = ~0;
	public float groundCheckPadding = 0.05f;

	[Tooltip("Maximum slope angle (degrees) that still counts as ground. Steeper surfaces are treated as walls.")]
	[Range(10f, 89f)] public float maxGroundSlopeAngle = 60f;

	[Header("Wall Interaction")]
	[Tooltip("How far ahead to check for walls while airborne to project movement along the surface.")]
	public float wallCheckDistance = 0.25f;

	[Header("View")]
	public Transform cameraTransform;
	[Range(0.1f, 20f)] public float mouseSensitivity = 3f;
	[Range(0.01f, 25f)] public float lookSmoothing = 12f;

	[Header("Cursor")]
	[Tooltip("If true, this script will lock the cursor on Awake and unlock on disable. Recommended OFF when using Mirror; LocalPlayerGate will manage the cursor for the local player only.")]
	public bool lockCursorOnAwake = false;

	Rigidbody _rb;
	CapsuleCollider _capsule;

	float _yaw;
	float _pitch;
	float _targetPitch;
	float _targetYaw;

	bool _isGrounded;
	float _lastGroundedTime;
	bool _wantJump;

	bool _isCrouching;
	float _originalCapsuleHeight;
	Vector3 _originalCapsuleCenter;
	Vector3 _originalCameraLocalPos;

	const float k_MaxPitch = 89f;
	const float k_MinPitch = -89f;
	const float k_CrouchLerpSpeed = 14f;

	void Awake()
	{
		_rb = GetComponent<Rigidbody>();
		if (_rb != null)
		{
			_rb.freezeRotation = true;
			if (_rb.interpolation == RigidbodyInterpolation.None)
				_rb.interpolation = RigidbodyInterpolation.Interpolate;
		}

		_capsule = GetComponent<CapsuleCollider>();
		if (_capsule == null)
			_capsule = GetComponentInChildren<CapsuleCollider>();

		if (_capsule != null)
		{
			_originalCapsuleHeight = _capsule.height;
			_originalCapsuleCenter = _capsule.center;
		}

		if (cameraTransform == null)
		{
			var cam = GetComponentInChildren<Camera>(true);
			if (cam != null) cameraTransform = cam.transform;
		}
		if (cameraTransform != null)
			_originalCameraLocalPos = cameraTransform.localPosition;

		_yaw = _targetYaw = transform.eulerAngles.y;
		if (cameraTransform != null)
			_pitch = _targetPitch = NormalizeAngle(cameraTransform.localEulerAngles.x);

		if (lockCursorOnAwake)
			LockCursor(true);
	}

	void OnDisable()
	{
		if (lockCursorOnAwake)
			LockCursor(false);
	}

	void Update()
	{
		ReadLookInput(out float mouseX, out float mouseY);
		_targetYaw += mouseX * mouseSensitivity;
		_targetPitch -= mouseY * mouseSensitivity;
		_targetPitch = Mathf.Clamp(_targetPitch, k_MinPitch, k_MaxPitch);

		float lerp = 1f - Mathf.Exp(-lookSmoothing * Time.unscaledDeltaTime);
		_yaw = Mathf.LerpAngle(_yaw, _targetYaw, lerp);
		_pitch = Mathf.Lerp(_pitch, _targetPitch, lerp);

		transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
		if (cameraTransform != null)
			cameraTransform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

		bool crouchHeld = IsKeyHeld(crouchKey);
		if (crouchHeld != _isCrouching)
			_isCrouching = crouchHeld;

		if (GetJumpPressed())
			_wantJump = true;

		if (_capsule != null)
		{
			float targetH = _isCrouching ? _originalCapsuleHeight * Mathf.Clamp01(crouchHeightMultiplier) : _originalCapsuleHeight;
			float newH = Mathf.Lerp(_capsule.height, targetH, 1f - Mathf.Exp(-k_CrouchLerpSpeed * Time.deltaTime));
			float heightDelta = _capsule.height - newH;
			Vector3 center = _capsule.center;
			center.y -= heightDelta * 0.5f;
			_capsule.height = newH;
			_capsule.center = center;
		}
		if (cameraTransform != null)
		{
			float yTarget = _isCrouching ? _originalCameraLocalPos.y * Mathf.Clamp01(crouchHeightMultiplier) : _originalCameraLocalPos.y;
			Vector3 camLocal = cameraTransform.localPosition;
			camLocal.y = Mathf.Lerp(camLocal.y, yTarget, 1f - Mathf.Exp(-k_CrouchLerpSpeed * Time.deltaTime));
			cameraTransform.localPosition = camLocal;
		}
	}

	void FixedUpdate()
	{
		_isGrounded = CheckGrounded();
		if (_isGrounded)
			_lastGroundedTime = Time.time;

		Vector2 move = ReadMoveInput();
		float speed = _isCrouching ? crouchSpeed : (IsKeyHeld(runKey) ? runSpeed : walkSpeed);

		Vector3 forward = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
		Vector3 right = new Vector3(transform.right.x, 0f, transform.right.z).normalized;
		Vector3 desired = (forward * move.y + right * move.x);
		if (desired.sqrMagnitude > 1f) desired.Normalize();
		desired *= speed;


		// If we're airborne and about to move into a wall, project our horizontal motion onto the wall plane
		Vector3 adjusted = desired;
		if (!_isGrounded && adjusted.sqrMagnitude > 0.0001f && _capsule != null)
		{
			if (TryCapsuleCastInDirection(adjusted.normalized, wallCheckDistance, out RaycastHit wallHit))
			{
				Transform t = _capsule.transform;
				Vector3 up = t.up;
				float slope = Vector3.Angle(wallHit.normal, up);
				if (slope > maxGroundSlopeAngle)
				{
					// Project the desired motion onto the collision plane to prevent pinning into the wall
					adjusted = Vector3.ProjectOnPlane(adjusted, wallHit.normal);
				}
			}
		}

		Vector3 vel = _rb.linearVelocity;
		vel.x = adjusted.x;
		vel.z = adjusted.z;

		if (_wantJump && CanJump())
		{
			_wantJump = false;
			vel.y = jumpVelocity;
		}
		_rb.linearVelocity = vel;
	}

	bool CanJump() => (Time.time - _lastGroundedTime) <= Mathf.Max(0f, coyoteTime);

	bool CheckGrounded()
	{
		if (_capsule == null)
		{
			float dist = 1.1f + groundCheckPadding;
			if (Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit rh, dist, groundMask, QueryTriggerInteraction.Ignore))
			{
				float slope = Vector3.Angle(rh.normal, Vector3.up);
				return slope <= maxGroundSlopeAngle;
			}
			return false;
		}

		Transform t = _capsule.transform;
		Vector3 scale = t.lossyScale;
		float radius = _capsule.radius * Mathf.Max(scale.x, scale.z) * 0.95f;
		float height = Mathf.Max(_capsule.height * scale.y, radius * 2f);
		Vector3 centerWorld = t.TransformPoint(_capsule.center);
		Vector3 up = t.up;

		float castDist = (height * 0.5f) + groundCheckPadding + 0.05f;
		if (Physics.SphereCast(centerWorld, radius, -up, out RaycastHit hit, castDist, groundMask, QueryTriggerInteraction.Ignore))
		{
			float slope = Vector3.Angle(hit.normal, up);
			if (slope > maxGroundSlopeAngle)
				return false;
			float distToBottom = hit.distance - (height * 0.5f);
			return distToBottom <= (groundCheckPadding + 0.02f);
		}
		return false;
	}

	bool TryCapsuleCastInDirection(Vector3 dir, float distance, out RaycastHit hit)
	{
		hit = default;
		if (_capsule == null) return false;
		Transform t = _capsule.transform;
		Vector3 scale = t.lossyScale;
		float radius = _capsule.radius * Mathf.Max(scale.x, scale.z) * 0.95f;
		float height = Mathf.Max(_capsule.height * scale.y, radius * 2f);
		Vector3 centerWorld = t.TransformPoint(_capsule.center);
		Vector3 up = t.up;
		Vector3 top = centerWorld + up * (height * 0.5f - radius);
		Vector3 bottom = centerWorld - up * (height * 0.5f - radius);
		return Physics.CapsuleCast(bottom, top, radius * 0.98f, dir, out hit, distance, groundMask, QueryTriggerInteraction.Ignore);
	}

	void ReadLookInput(out float mouseX, out float mouseY)
	{
		mouseX = 0f; mouseY = 0f;
#if ENABLE_INPUT_SYSTEM
		var mouse = UnityEngine.InputSystem.Mouse.current;
		if (mouse != null)
		{
			var delta = mouse.delta.ReadValue();
			mouseX = delta.x * 0.02f;
			mouseY = delta.y * 0.02f;
		}
#endif
		mouseX += Input.GetAxisRaw("Mouse X");
		mouseY += Input.GetAxisRaw("Mouse Y");
	}

	Vector2 ReadMoveInput()
	{
#if ENABLE_INPUT_SYSTEM
		var kb = UnityEngine.InputSystem.Keyboard.current;
		if (kb != null)
		{
			float x = (kb.dKey.isPressed ? 1f : 0f) + (kb.aKey.isPressed ? -1f : 0f);
			float y = (kb.wKey.isPressed ? 1f : 0f) + (kb.sKey.isPressed ? -1f : 0f);
			Vector2 v = new Vector2(x, y);
			return v.sqrMagnitude > 1f ? v.normalized : v;
		}
#endif
		float horiz = Input.GetAxisRaw("Horizontal");
		float vert = Input.GetAxisRaw("Vertical");
		Vector2 mv = new Vector2(horiz, vert);
		return mv.sqrMagnitude > 1f ? mv.normalized : mv;
	}

	bool GetJumpPressed()
	{
#if ENABLE_INPUT_SYSTEM
		var kb = UnityEngine.InputSystem.Keyboard.current;
		if (kb != null)
		{
			if (jumpKey == KeyCode.Space && kb.spaceKey.wasPressedThisFrame)
				return true;
		}
#endif
		return Input.GetKeyDown(jumpKey);
	}

	bool IsKeyHeld(KeyCode code)
	{
#if ENABLE_INPUT_SYSTEM
		var kb = UnityEngine.InputSystem.Keyboard.current;
		if (kb != null)
		{
			switch (code)
			{
				case KeyCode.LeftShift: if (kb.leftShiftKey.isPressed) return true; break;
				case KeyCode.RightShift: if (kb.rightShiftKey.isPressed) return true; break;
				case KeyCode.LeftControl: if (kb.leftCtrlKey.isPressed) return true; break;
				case KeyCode.RightControl: if (kb.rightCtrlKey.isPressed) return true; break;
				case KeyCode.Space: if (kb.spaceKey.isPressed) return true; break;
			}
		}
#endif
		return Input.GetKey(code);
	}

	static float NormalizeAngle(float angle)
	{
		angle %= 360f;
		if (angle > 180f) angle -= 360f;
		return angle;
	}

	void LockCursor(bool locked)
	{
		Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
		Cursor.visible = !locked;
	}

#if UNITY_EDITOR
	void OnDrawGizmosSelected()
	{
		if (_capsule == null) return;
		Transform t = _capsule.transform;
		Vector3 scale = t.lossyScale;
		float radius = _capsule.radius * Mathf.Max(scale.x, scale.z) * 0.95f;
		float height = Mathf.Max(_capsule.height * scale.y, radius * 2f);
		Vector3 centerWorld = t.TransformPoint(_capsule.center);
		Vector3 up = t.up;
		Vector3 bottom = centerWorld - up * (height * 0.5f - radius - groundCheckPadding);
		Gizmos.color = Color.green;
		Gizmos.DrawWireSphere(bottom, radius);
	}
#endif
}

