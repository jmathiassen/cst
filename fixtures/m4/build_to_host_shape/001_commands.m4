AC_DEFUN([gl_BUILD_TO_HOST],
[
  AC_REQUIRE([gl_BUILD_TO_HOST_INIT])
  AC_CONFIG_COMMANDS([build-to-host], [eval $gl_config_gt | $SHELL], [gl_config_gt=true])
])
