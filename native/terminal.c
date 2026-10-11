#define _GNU_SOURCE
#include <pty.h>
#include <poll.h>
#include <signal.h>
#include <sys/wait.h>
#include <sys/ioctl.h>
#include <unistd.h>
#include <errno.h>
#include <stdlib.h>
#include <stdio.h>
static volatile sig_atomic_t child=0;
static void forward(int sig) { if(child>0)kill(-child,sig); }
static int write_all(int fd,const char* data,ssize_t len) {while(len>0){ssize_t n=write(fd,data,len);if(n<0){if(errno==EINTR)continue;return -1;}data+=n;len-=n;}return 0;}
int main(int argc,char** argv) {
 if(argc<2)return 64;
 struct winsize size={40,140,0,0};int master;pid_t pid=forkpty(&master,NULL,NULL,&size);
 if(pid<0)return 70;
 if(pid==0){execv(argv[1],argv+1);perror("execv");_exit(127);}
 child=pid;signal(SIGTERM,forward);signal(SIGINT,forward);signal(SIGHUP,forward);
 struct pollfd fds[2]={{STDIN_FILENO,POLLIN,0},{master,POLLIN,0}};char buffer[65536];
 for(;;) {
  if(poll(fds,2,-1)<0){if(errno==EINTR)continue;break;}
  if(fds[0].revents&POLLIN){ssize_t n=read(0,buffer,sizeof buffer);if(n<=0)fds[0].fd=-1;else if(write_all(master,buffer,n)<0)break;}
  if(fds[0].revents&POLLHUP)fds[0].fd=-1;
  if(fds[1].revents&(POLLIN|POLLHUP)){ssize_t n=read(master,buffer,sizeof buffer);if(n<=0)break;if(write_all(1,buffer,n)<0)break;}
 }
 close(master);int status;while(waitpid(pid,&status,0)<0&&errno==EINTR){}return WIFEXITED(status)?WEXITSTATUS(status):128+WTERMSIG(status);
}
