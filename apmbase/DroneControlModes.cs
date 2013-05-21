using System;


namespace apmbase
{

	public enum apmmodes
	{
		MANUAL = 0,
		CIRCLE = 1,
		STABILIZE = 2,
		TRAINING = 3,
		FLY_BY_WIRE_A = 5,
		FLY_BY_WIRE_B = 6,
		AUTO = 10,
		RTL = 11,
		LOITER = 12,
		GUIDED = 15,

		TAKEOFF = 99
	}

	public enum aprovermodes
	{
		MANUAL = 0,
		LEARNING = 2,
		STEERING = 3,
		HOLD = 4,
		AUTO = 10,
		RTL = 11,
		GUIDED = 15,
		INITIALISING = 16
	}

	public enum ac2modes
	{
		STABILIZE = 0,			// hold level position
		ACRO = 1,			// rate control
		ALT_HOLD = 2,		// AUTO control
		AUTO = 3,			// AUTO control
		GUIDED = 4,		// AUTO control
		LOITER = 5,		// Hold a single location
		RTL = 6,				// AUTO control
		CIRCLE = 7,
		POSITION = 8,
		LAND = 9,				// AUTO control
		OF_LOITER = 10,
		TOY = 11
	}



	public enum ac2ch6modes
	{
		// CH_6 Tuning
		// -----------
		CH6_0NONE = 0,  // no tuning performed
		CH6_STABILIZE_KP = 1,   // stabilize roll/pitch angle controller's P term
		CH6_STABILIZE_KI = 2,   // stabilize roll/pitch angle controller's I term
		CH6_STABILIZE_KD = 29,   // stabilize roll/pitch angle controller's D term
		CH6_YAW_KP = 3,   // stabilize yaw heading controller's P term
		CH6_YAW_KI = 24,   // stabilize yaw heading controller's P term
		CH6_ACRO_KP = 25,   // acro controller's P term.  converts pilot input to a desired roll, pitch or yaw rate
		CH6_RATE_KP = 4,   // body frame roll/pitch rate controller's P term
		CH6_RATE_KI = 5,   // body frame roll/pitch rate controller's I term
		CH6_RATE_KD = 21,   // body frame roll/pitch rate controller's D term
		CH6_YAW_RATE_KP = 6,   // body frame yaw rate controller's P term
		CH6_YAW_RATE_KD = 26,   // body frame yaw rate controller's D term
		CH6_THR_HOLD_KP = 14,   // altitude hold controller's P term (alt error to desired rate)
		CH6_THROTTLE_KP = 7,   // throttle rate controller's P term (desired rate to acceleration or motor output)
		CH6_THROTTLE_KI = 33,   // throttle rate controller's I term (desired rate to acceleration or motor output)
		CH6_THR_ACCEL_KP = 34,   // accel based throttle controller's P term
		CH6_THR_ACCEL_KI = 35,   // accel based throttle controller's I term
		CH6_THR_ACCEL_KD = 36,   // accel based throttle controller's D term
		CH6_TOP_BOTTOM_RATIO = 8,   // upper/lower motor ratio (not used)
		CH6_RELAY = 9,   // switch relay on if ch6 high, off if low
		CH6_TRAVERSE_SPEED = 10,   // maximum speed to next way point (0 to 10m/s)
		CH6_NAV_KP = 11,   // navigation rate controller's P term (speed error to tilt angle)
		CH6_NAV_KI = 20,   // navigation rate controller's I term (speed error to tilt angle)
		CH6_LOITER_KP = 12,   // loiter distance controller's P term (position error to speed)
		CH6_LOITER_KI = 27,   // loiter distance controller's I term (position error to speed)
		CH6_HELI_EXTERNAL_GYRO = 13,   // TradHeli specific external tail gyro gain
		CH6_OPTFLOW_KP = 17,   // optical flow loiter controller's P term (position error to tilt angle)
		CH6_OPTFLOW_KI = 18,   // optical flow loiter controller's I term (position error to tilt angle)
		CH6_OPTFLOW_KD = 19,   // optical flow loiter controller's D term (position error to tilt angle)
		CH6_LOITER_RATE_KP = 22,   // loiter rate controller's P term (speed error to tilt angle)
		CH6_LOITER_RATE_KI = 28,   // loiter rate controller's I term (speed error to tilt angle)
		CH6_LOITER_RATE_KD = 23,   // loiter rate controller's D term (speed error to tilt angle)
		CH6_AHRS_YAW_KP = 30,   // ahrs's compass effect on yaw angle (0 = very low, 1 = very high)
		CH6_AHRS_KP = 31,   // accelerometer effect on roll/pitch angle (0=low)
		CH6_INAV_TC = 32,          // inertial navigation baro/accel and gps/accel time
		CH6_DECLINATION = 38,         // compass declination in radians
		CH6_CIRCLE_RATE = 39,        // circle turn rate in degrees (hard coded to about 45 degrees in either direction)
	}

	public enum ac2ch7modes
	{
		CH7_DO_NOTHING = 0,
		CH7_FLIP = 2,
		CH7_SIMPLE_MODE = 3,
		CH7_RTL = 4,
		CH7_AUTO_TRIM = 5,
		CH7_SAVE_WP = 7,
		CH7_CAMERA_TRIGGER = 9,
		CH7_SONAR = 10
	}

}

