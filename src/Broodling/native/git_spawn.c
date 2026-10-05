#define _GNU_SOURCE
#include <errno.h>
#include <fcntl.h>
#include <stdint.h>
#include <sys/stat.h>
#include <unistd.h>

_Static_assert(sizeof(int) == sizeof(int32_t), "int width");

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
    /* flock needs no write access, and the target is the SQLite store: this description must never gain write access. */
    int fd = above_stdio(open(path, O_RDONLY | O_CLOEXEC | O_NOFOLLOW | O_NONBLOCK));
    if (fd < 0) return errno;
    struct stat st;
    int error = fstat(fd, &st) == 0 ? 0 : errno;
    if (!error && (!S_ISREG(st.st_mode) || st.st_nlink != 1)) error = EINVAL;
    if (error) { close(fd); return error; }
    *fd_out = fd;
    return 0;
}
