namespace A2A;

internal sealed class PushNotificationAdmissionException(Exception? innerException = null)
    : Exception("Push notification delivery could not be accepted.", innerException);
