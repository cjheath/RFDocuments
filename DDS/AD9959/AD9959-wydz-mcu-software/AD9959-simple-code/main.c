//AD9959不同频率，不同幅度，不同相位测试程序
//编写者：刘涛
//日期：20140829
 /*-----------------------------------------------
  名称：AD9959串行驱动
  编写：Liu
  日期：2014.8
  修改：无
  内容：
 
------------------------------------------------*/
#include <stc15fxxxx.h>
//宏定义
#define uchar unsigned char
#define uint  unsigned int	
#define ulong  unsigned long int
//-------------- 函数申明 --------------------------------
void delay1 (ulong  length);
void delay2 (void);
void IO_Update(void);
void WriteData_AD9959(uchar RegisterAddress, uchar NumberofRegisters, uchar *RegisterData,uchar temp); //写控制字
void Write_frequence(uchar Channel,ulong Freq);//不同通道，不同频率
void Write_Amplitude(uchar Channel, unsigned  int  Ampli);//不同通道，不同幅度
void Write_Phase(uchar Channel,unsigned  int  Phase);//不同通道，不同相位						
//IO口定义
sbit CS = P2^5;
sbit SCLK = P0^2;
sbit SDIO0= P0^0;
sbit UPDATE = P2^4;
sbit PS0 = P1^3;
sbit PS1 = P1^4;
sbit PS2 = P2^6;
sbit PS3 = P2^7;
sbit SDIO1 = P0^1;
sbit SDIO2 = P1^1;
sbit SDIO3 = P1^0;
sbit Reset = P1^2;
sbit PWR = P4^5;
//初始化频率
ulong Frequence  = 20000000;	//频率，单位HZ
ulong Frequence0 = 10000000;	//频率0，单位HZ
ulong Frequence1 = 20000000;	//频率1，单位HZ
ulong Frequence2 = 30000000;	//频率2，单位HZ
ulong Frequence3 = 40000000;	//频率3，单位HZ


//AD9959内寄存器定义列表
//-----------------------------------------------------------------------------------------------------------------
#define CSR_ADD  0x00       //CSR 通道选择寄存器，包括通道选择，串行 3 线 通信模式，数据传输首先高低位设置
                                                 // default Value = 0xF0 详细请参见AD9958 datasheet Table 27	
																			        
uchar CSR_DATA0[1] = {0x10};     // 开 CH0
uchar CSR_DATA1[1] = {0x20};      // 开 CH1
uchar CSR_DATA2[1] = {0x40};      // 开 CH2
uchar CSR_DATA3[1] = {0x80};      // 开 CH3		
																	
#define FR1_ADD  0x01   //FR1 功能寄存器1,详细请参见AD9958 datasheet Table 27																		
uchar FR1_DATA[3] = {0xD0,0x00,0x00};//default Value = 0x000000;   20倍频;  Charge pump control = 75uA
                                                  //FR1<23> -- VCO gain control =0时 system clock below 160 MHz; 
                                                  //             =1时, the high range (system clock above 255 MHz
#define FR2_ADD 0x02   //FR2 功能寄存器2 详细请参见AD9958 datasheet Table 27	
uchar FR2_DATA[2] = {0x00,0x00};//default Value = 0x0000

#define CFR_ADD 0x03   //CFR 通道功能寄存器,详细请参见AD9958 datasheet Table 28																		
uchar CFR_DATA[3] = {0x00,0x03,0x02};//default Value = 0x000302	   

#define CFTW0_ADD 0x04   //CTW0 通道频率转换字寄存器,详细请参见AD9958 datasheet Table 28		
uchar CFTW0_DATA0[4] = {0x33,0x33,0x33,0x33};   //OUT0 100MHZ	   主频500M
uchar CFTW0_DATA1[4] = {0x28,0xF5,0xC2,0x8F};  //OUT1 80MHZ 
uchar CFTW0_DATA2[4] = {0x05,0x1E,0xB8,0x52};  //OUT2 10MHZ
uchar CFTW0_DATA3[4] = {0x00,0x83,0x12,0x6F};   //OUT3 1MHZ	

#define CPOW0_ADD 0x05   //CPW0 通道相位转换字寄存器,详细请参见AD9958 datasheet Table 28																		
uchar CPOW0_DATA[2] = {0x00,0x00};//default Value = 0x0000   @ = POW/2^14*360

#define ACR_ADD 0x06   //ACR 幅度控制寄存器,详细请参见AD9958 datasheet Table 28																		
uchar ACR_DATA[3] = {0x00,0x00,0x00};//default Value = 0x--0000 Rest = 18.91/Iout 

#define LSRR_ADD 0x07   //LSR 通道线性扫描寄存器,详细请参见AD9958 datasheet Table 28																		
uchar LSRR_DATA[2] = {0x00,0x00};//default Value = 0x----


#define RDW_ADD 0x08   //RDW 通道线性向上扫描寄存器,详细请参见AD9958 datasheet Table 28																		
uchar RDW_DATA[4] = {0x00,0x00,0x00,0x00};//default Value = 0x--------

#define FDW_ADD 0x09   //FDW 通道线性向下扫描寄存器,详细请参见AD9958 datasheet Table 28																		
uchar FDW_DATA[4] = {0x00,0x00,0x00,0x00};//default Value = 0x--------

void delay1 (unsigned long   length)
{
	length = length*12;
   while(length--);
}

void IntReset(void)	  //AD9910复位
{
    Reset = 0;
	delay1(1);
	Reset = 1;
	delay1(30);
	Reset = 0;
}

void IO_Update(void)   //AD9959更新数据
{
	UPDATE = 0;
	delay1(2);
	UPDATE = 1;
	delay1(4);
	UPDATE = 0;
}

void Intserve(void)		   //IO口初始化
{   PWR=0;
    CS = 1;
    SCLK = 0;
    UPDATE = 0;
    PS0 = 0;
    PS1 = 0;
    PS2 = 0;
    PS3 = 0;
    
    SDIO0= 0;
    SDIO1 = 0;
    SDIO2 = 0;
    SDIO3 = 0;
}


//---------------------------------
//
//---------------------------------
//函数功能：控制器通过SPI向AD9959写数据

//RegisterAddress ---- 寄存器地址
//NumberofRegisters---- 所含字节数
//*RegisterData ----- 数据起始地址
//temp ----- 是否更新IO寄存器
//--------------------------------------------------------------------------------
//--------------------------------------------------------------------------------
void WriteData_AD9959(uchar RegisterAddress, uchar NumberofRegisters, uchar *RegisterData,uchar temp)
{
	uchar	ControlValue = 0;
	uchar		ValueToWrite = 0;
	uchar	RegisterIndex = 0;
	uchar	i = 0;

	//Create the 8-bit header
	ControlValue = RegisterAddress;

	SCLK = 0;
	CS = 0;	 //bring CS low
	//Write out the control word
	for(i=0; i<8; i++)
	{
		SCLK = 0;
		if(0x80 == (ControlValue & 0x80))
		{
			SDIO0= 1;	  //Send one to SDIO0pin
		}
		else
		{
			SDIO0= 0;	  //Send zero to SDIO0pin
		}
		SCLK = 1;
		ControlValue <<= 1;	//Rotate data
	}
	SCLK = 0;
	//And then the data
	for (RegisterIndex=0; RegisterIndex<NumberofRegisters; RegisterIndex++)
	{
		ValueToWrite = RegisterData[RegisterIndex];
		for (i=0; i<8; i++)
		{
			SCLK = 0;
			if(0x80 == (ValueToWrite & 0x80))
			{
				SDIO0= 1;	  //Send one to SDIO0pin
			}
			else
			{
				SDIO0= 0;	  //Send zero to SDIO0pin
			}
			SCLK = 1;
			ValueToWrite <<= 1;	//Rotate data
		}
		SCLK = 0;		
	}	
	if(temp==1)
	  {
	  IO_Update();
	  }	
  CS = 1;	//bring CS high again
} 
void Init_AD9959(void)  
{ Intserve();
  IntReset();
 // WriteData_AD9959(CSR_ADD,1,CSR_DATA0,1);
  WriteData_AD9959(FR1_ADD,3,FR1_DATA,1);
 // WriteData_AD9959(FR2_ADD,2,FR2_DATA,0);
//  WriteData_AD9959(CFR_ADD,3,CFR_DATA,1);
  WriteData_AD9959(CFTW0_ADD,4,CFTW0_DATA0,1);
  //WriteData_AD9959(CPOW0_ADD,2,CPOW0_DATA,0);
 // WriteData_AD9959(ACR_ADD,3,ACR_DATA,0);
 // WriteData_AD9959(LSRR_ADD,2,LSRR_DATA,0);
 // WriteData_AD9959(RDW_ADD,2,RDW_DATA,0);
//  WriteData_AD9959(FDW_ADD,4,FDW_DATA,1);

          WriteData_AD9959(CSR_ADD,1,CSR_DATA0,1);//控制寄存器写入CH0通道
          WriteData_AD9959(CFTW0_ADD,4,CFTW0_DATA0,1);//CTW0 address 0x04.输出CH0设定频率

		  WriteData_AD9959(CSR_ADD,1,CSR_DATA1,1);//控制寄存器写入CH1通道
          WriteData_AD9959(CFTW0_ADD,4,CFTW0_DATA1,1);//CTW0 address 0x04.输出CH1设定频率
		  	
		  WriteData_AD9959(CSR_ADD,1,CSR_DATA2,1);//控制寄存器写入CH1通道
          WriteData_AD9959(CFTW0_ADD,4,CFTW0_DATA2,1);//CTW0 address 0x04.输出CH1设定频率
		  	
		  WriteData_AD9959(CSR_ADD,1,CSR_DATA3,1);//控制寄存器写入CH1通道
          WriteData_AD9959(CFTW0_ADD,4,CFTW0_DATA3,1);//CTW0 address 0x04.输出CH1设定频率	

} 
  
//=====================================================================
//===================计算频偏字、频率字和发送程序======================
void Write_frequence(uchar Channel,ulong Freq)
{	 uchar CFTW0_DATA[4] ={0x00,0x00,0x00,0x00};	//中间变量
	  ulong Temp;            
	  Temp=(ulong)Freq*8.589934592;	   //将输入频率因子分为四个字节  4.294967296=(2^32)/500000000
	  CFTW0_DATA[3]=(uchar)Temp;
	  CFTW0_DATA[2]=(uchar)(Temp>>8);
	  CFTW0_DATA[1]=(uchar)(Temp>>16);
	  CFTW0_DATA[0]=(uchar)(Temp>>24);
	  if(Channel==0)	  
	     {WriteData_AD9959(CSR_ADD,1,CSR_DATA0,1);//控制寄存器写入CH0通道
          WriteData_AD9959(CFTW0_ADD,4,CFTW0_DATA,1);//CTW0 address 0x04.输出CH0设定频率
		 }
	  if(Channel==1)	
	   {WriteData_AD9959(CSR_ADD,1,CSR_DATA1,1);//控制寄存器写入CH1通道
          WriteData_AD9959(CFTW0_ADD,4,CFTW0_DATA,1);//CTW0 address 0x04.输出CH1设定频率	
	   }
	  if(Channel==2)	
	   {WriteData_AD9959(CSR_ADD,1,CSR_DATA2,1);//控制寄存器写入CH2通道
          WriteData_AD9959(CFTW0_ADD,4,CFTW0_DATA,1);//CTW0 address 0x04.输出CH2设定频率	
	   }
	  if(Channel==3)	
	   {WriteData_AD9959(CSR_ADD,1,CSR_DATA3,1);//控制寄存器写入CH3通道
        WriteData_AD9959(CFTW0_ADD,4,CFTW0_DATA,3);//CTW0 address 0x04.输出CH3设定频率	
	   }																																																																										 
	
} 

//==============更新幅度====================================

void Write_Amplitude(uchar Channel, unsigned  int  Ampli)
{ uint A_temp;//=0x23ff;
  A_temp=Ampli|0x1000;
 ACR_DATA[2]=(uchar)A_temp;  //低位数据
   ACR_DATA[1]=(uchar)(A_temp>>8); //高位数据
  if(Channel==0)
     {WriteData_AD9959(CSR_ADD,1,CSR_DATA0,1); //控制寄存器写入CH0通道
      WriteData_AD9959(ACR_ADD,3,ACR_DATA,1); //
	  }
  if(Channel==1)
     {WriteData_AD9959(CSR_ADD,1,CSR_DATA1,1); //控制寄存器写入CH1通道
      WriteData_AD9959(ACR_ADD,3,ACR_DATA,1); //
	 }
  if(Channel==2)
     {WriteData_AD9959(CSR_ADD,1,CSR_DATA2,1); //控制寄存器写入CH2通道
      WriteData_AD9959(ACR_ADD,3,ACR_DATA,1); //
	 }
  if(Channel==3)
     {WriteData_AD9959(CSR_ADD,1,CSR_DATA3,1); //控制寄存器写入CH3通道
      WriteData_AD9959(ACR_ADD,3,ACR_DATA,1); //
	 }
} 
void Write_Phase(uchar Channel,unsigned  int  Phase)
{uint P_temp=0;
 P_temp=(uint)Phase*45.511111;//将输入相位差写入，进度1度，45.511111=2^14）/360
 CPOW0_DATA[1]=(uchar)P_temp;
 CPOW0_DATA[0]=(uchar)(P_temp>>8);
 if(Channel==0)
  {WriteData_AD9959(CSR_ADD,1,CSR_DATA0,0); //控制寄存器写入CH0通道																																						   //CH0 关闭 and CH1 打开 数据传输从高位至地位	
   WriteData_AD9959(CPOW0_ADD,2,CPOW0_DATA,0);
  }
  if(Channel==1)
  {WriteData_AD9959(CSR_ADD,1,CSR_DATA1,0); //控制寄存器写入CH0通道																																						   //CH0 关闭 and CH1 打开 数据传输从高位至地位	
   WriteData_AD9959(CPOW0_ADD,2,CPOW0_DATA,0);
  }
  if(Channel==2)
  {WriteData_AD9959(CSR_ADD,1,CSR_DATA2,0); //控制寄存器写入CH0通道																																						   //CH0 关闭 and CH1 打开 数据传输从高位至地位	
   WriteData_AD9959(CPOW0_ADD,2,CPOW0_DATA,0);
  }
  if(Channel==3)
  {WriteData_AD9959(CSR_ADD,1,CSR_DATA3,0); //控制寄存器写入CH0通道																																						   //CH0 关闭 and CH1 打开 数据传输从高位至地位	
   WriteData_AD9959(CPOW0_ADD,2,CPOW0_DATA,0);
  }
}	 
void main()
{
	Init_AD9959();
	delay1(10000);
	Write_frequence(0,Frequence0); Write_frequence(1,Frequence1);Write_frequence(2,Frequence2);Write_frequence(3,Frequence3);
	delay1(10000);
    Write_frequence(0,Frequence); Write_frequence(1,Frequence);Write_frequence(2,Frequence);Write_frequence(3,Frequence);
	Write_Amplitude(0,1023); Write_Amplitude(1,1023); 	Write_Amplitude(2,1023); Write_Amplitude(3,1023);
    Write_Phase(0,0); Write_Phase(1,0);   Write_Phase(2,0); Write_Phase(3,0);
	while(1)
	{;
	}
}