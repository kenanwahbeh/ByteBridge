using System.ComponentModel;

namespace ByteBridge.Gateway;

/*
 * Which failures mean "the service", as opposed to a fault somewhere
 * else that happened while the service was involved.
 *
 * Both of these were private methods on the panel's service control, so
 * neither could be asked a question by a test. They decide whether to
 * tell the operator the gateway is broken or to report an error that
 * has nothing to do with them -- and every exception thrown by
 * ServiceController is constructed by the framework, so there was no way
 * to find out which bucket one landed in without a Windows machine and a
 * missing service.
 */
public static class ServiceFailures
{
    /*
     * What ServiceController throws, and what it throws when the service
     * is simply not there: Win32Exception from the SCM, TimeoutException
     * when a start or stop does not finish in time,
     * InvalidOperationException when the name does not resolve.
     *
     * Matched on System.TimeoutException rather than
     * System.ServiceProcess.TimeoutException because the latter derives
     * from the former and lives in a Windows-only assembly this project
     * does not reference -- it could not be named here at all. The
     * match is therefore wider than the original: any TimeoutException
     * counts, which is right for the two call sites, both of which are
     * a start or a stop that ran out of time.
     */
    public static bool IsServiceFailure(Exception error) =>
        error is Win32Exception
        || error is TimeoutException
        || error is InvalidOperationException;

    /*
     * Narrower, for the questions about the service rather than about
     * starting it: whether the panel may try at all. A timeout here
     * means something else was slow, not that the service misbehaved.
     */
    public static bool IsServiceControlFailure(Exception error) =>
        error is Win32Exception
        || error is InvalidOperationException;
}