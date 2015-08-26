#ifdef __cplusplus
extern "C"
{
#endif
	
#include <ets_sys.h>
#include <osapi.h>
#include <gpio.h>
#include <os_type.h>
#include <user_interface.h>
#include <espmissingincludes.h>

int user_init();

#ifdef __cplusplus
}
#endif

static os_timer_t s_Timer;
int s_Tick = 0;

void TimerFunction(void *arg)
{
	s_Tick++;

	//Uncomment the line below to disable the software watchdog that will restart the ESP8266 system after it spends more than ~1 second stopped at a breakpoint.
	//system_soft_wdt_stop();
	
	if (GPIO_REG_READ(GPIO_OUT_ADDRESS) & BIT1)
	{
		gpio_output_set(0, BIT1, BIT1, 0);
	}
	else
	{
		gpio_output_set(BIT1, 0, BIT1, 0);
	}
}

int user_init()
{
	gpio_init();

	PIN_FUNC_SELECT(PERIPHS_IO_MUX_U0TXD_U, FUNC_GPIO1);

	gpio_output_set(0, BIT1, BIT1, 0);
	os_timer_setfn(&s_Timer, TimerFunction, NULL);
	os_timer_arm(&s_Timer, $$com.sysprogs.esp8266.ledblink.DELAYMSEC$$, 1);
    
	return 0;
}
