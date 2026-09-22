#define _GNU_SOURCE
#include <errno.h>
#include <fcntl.h>
#include <signal.h>
#include <spawn.h>
#include <stdint.h>
#include <sys/stat.h>
#include <unistd.h>

_Static_assert(sizeof(pid_t) == sizeof(int32_t), "pid_t width");
_Static_assert(sizeof(int) == sizeof(int32_t), "int width");

/* No signal changes or reaping here. The managed caller owns the positive PID. */
int32_t broodling_check_host(void)
{
    struct sigaction action;
    if (getpid() == 1) return ENOTSUP;
    if (sigaction(SIGCHLD, NULL, &action) != 0) return errno;
    if (action.sa_handler == SIG_IGN || (action.sa_flags & SA_NOCLDWAIT)) return ENOTSUP;
    return 0;
}

static int above_stdio(int fd)
{
    if (fd < 0 || fd > 2) return fd;
    int copy = fcntl(fd, F_DUPFD_CLOEXEC, 3);
    int error = errno;
    close(fd);
    errno = error;
    return copy;
}

int32_t broodling_open_lock(const char *path, int32_t *fd_out)
{
    *fd_out = -1;
    int fd = above_stdio(open(path, O_CREAT | O_RDWR | O_CLOEXEC | O_NOFOLLOW | O_NONBLOCK, 0600));
    if (fd < 0) return errno;
    struct stat st;
    int error = fstat(fd, &st) == 0 ? 0 : errno;
    if (!error && (!S_ISREG(st.st_mode) || st.st_nlink != 1)) error = EINVAL;
    if (error) { close(fd); return error; }
    *fd_out = fd;
    return 0;
}

static int make_pipe(int fds[2])
{
    if (pipe2(fds, O_CLOEXEC) != 0) return errno;
    for (int i = 0; i < 2; i++) {
        fds[i] = above_stdio(fds[i]);
        if (fds[i] < 0) return errno;
    }
    return 0;
}

/* All descriptors stay CLOEXEC in the parent, including throughout spawn.
 * dup2 file actions clear CLOEXEC only in the selected child. */
int32_t broodling_spawn_git(const char *path, char *const argv[], char *const envp[],
                            int32_t lock_fd, int32_t *pid_out,
                            int32_t *stdout_out, int32_t *stderr_out)
{
    *pid_out = *stdout_out = *stderr_out = -1;
    int output[2] = {-1, -1}, error_pipe[2] = {-1, -1}, input = -1;
    int error = broodling_check_host();
    if (error) return error;
    if (lock_fd < 3 || fcntl(lock_fd, F_GETFD) < 0) return EBADF;
    if ((error = make_pipe(output)) || (error = make_pipe(error_pipe))) goto cleanup;
    input = above_stdio(open("/dev/null", O_RDONLY | O_CLOEXEC));
    if (input < 0) { error = errno; goto cleanup; }
    posix_spawn_file_actions_t actions;
    error = posix_spawn_file_actions_init(&actions);
    if (error) goto cleanup;
    int sources[] = {input, output[1], error_pipe[1]};
    for (int i = 0; !error && i < 3; i++)
        error = posix_spawn_file_actions_adddup2(&actions, sources[i], i);
    if (!error) error = posix_spawn_file_actions_adddup2(&actions, lock_fd, lock_fd);
    int closing[] = {input, output[0], output[1], error_pipe[0], error_pipe[1]};
    for (int i = 0; !error && i < 5; i++)
        error = posix_spawn_file_actions_addclose(&actions, closing[i]);
    pid_t pid = -1;
    if (!error) error = posix_spawn(&pid, path, &actions, NULL, argv, envp);
    posix_spawn_file_actions_destroy(&actions);
    if (!error) {
        *pid_out = pid;
        *stdout_out = output[0]; output[0] = -1;
        *stderr_out = error_pipe[0]; error_pipe[0] = -1;
    }
cleanup:
    if (input >= 0) close(input);
    for (int i = 0; i < 2; i++) {
        if (output[i] >= 0) close(output[i]);
        if (error_pipe[i] >= 0) close(error_pipe[i]);
    }
    return error;
}
