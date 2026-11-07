#include <EGL/egl.h>

// emscripten 的 EGL 实现没有 pbuffer 系列；托管侧只有 WINDOWS 无头渲染会调用，
// 这里给链接器补桩（返回 EGL_NO_SURFACE），浏览器运行期不会走到
EGLSurface eglCreatePbufferSurface(EGLDisplay dpy, EGLConfig config, const EGLint *attrib_list)
{
    (void)dpy; (void)config; (void)attrib_list;
    return EGL_NO_SURFACE;
}
