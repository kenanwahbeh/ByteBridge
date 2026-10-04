namespace ByteBridge.Enrollment;

/*
 * A failure the person running the command can act on, as opposed to a
 * bug. The message is shown as it is, so it says what to do next.
 *
 * Transient marks the ones worth retrying while waiting for approval: a
 * server that is briefly down must not abandon an enrolment that is
 * already sitting in the approver's inbox.
 */
public sealed class EnrollmentException : Exception
{
    public bool Transient { get; }

    public EnrollmentException(string message, bool transient = false)
        : base(message)
    {
        Transient = transient;
    }
}
