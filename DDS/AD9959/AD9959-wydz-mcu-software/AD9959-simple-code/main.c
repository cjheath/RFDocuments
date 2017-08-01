//AD9959��ͬƵ�ʣ���ͬ���ȣ���ͬ��λ���Գ���
//��д�ߣ�����
//���ڣ�20140829
 /*-----------------------------------------------
  ���ƣ�AD9959��������
  ��д��Liu
  ���ڣ�2014.8
  �޸ģ���
  ���ݣ�
 
------------------------------------------------*/
#include <stc15fxxxx.h>
//�궨��
#define uchar unsigned char
#define uint  unsigned int	
#define ulong  unsigned long int
//-------------- �������� --------------------------------
void delay1 (ulong  length);
void delay2 (void);
void IO_Update(void);
void WriteData_AD9959(uchar RegisterAddress, uchar NumberofRegisters, uchar *RegisterData,uchar temp); //д������
void Write_frequence(uchar Channel,ulong Freq);//��ͬͨ������ͬƵ��
void Write_Amplitude(uchar Channel, unsigned  int  Ampli);//��ͬͨ������ͬ����
void Write_Phase(uchar Channel,unsigned  int  Phase);//��ͬͨ������ͬ��λ						
//IO�ڶ���
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
//��ʼ��Ƶ��
ulong Frequence  = 20000000;	//Ƶ�ʣ���λHZ
ulong Frequence0 = 10000000;	//Ƶ��0����λHZ
ulong Frequence1 = 20000000;	//Ƶ��1����λHZ
ulong Frequence2 = 30000000;	//Ƶ��2����λHZ
ulong Frequence3 = 40000000;	//Ƶ��3����λHZ


//AD9959�ڼĴ��������б�
//-----------------------------------------------------------------------------------------------------------------
#define CSR_ADD  0x00       //CSR ͨ��ѡ��Ĵ���������ͨ��ѡ�񣬴��� 3 �� ͨ��ģʽ�����ݴ������ȸߵ�λ����
                                                 // default Value = 0xF0 ��ϸ��μ�AD9958 datasheet Table 27	
																			        
uchar CSR_DATA0[1] = {0x10};     // �� CH0
uchar CSR_DATA1[1] = {0x20};      // �� CH1
uchar CSR_DATA2[1] = {0x40};      // �� CH2
uchar CSR_DATA3[1] = {0x80};      // �� CH3		
																	
#define FR1_ADD  0x01   //FR1 ���ܼĴ���1,��ϸ��μ�AD9958 datasheet Table 27																		
uchar FR1_DATA[3] = {0xD0,0x00,0x00};//default Value = 0x000000;   20��Ƶ;  Charge pump control = 75uA
                                                  //FR1<23> -- VCO gain control =0ʱ system clock below 160 MHz; 
                                                  //             =1ʱ, the high range (system clock above 255 MHz
#define FR2_ADD 0x02   //FR2 ���ܼĴ���2 ��ϸ��μ�AD9958 datasheet Table 27	
uchar FR2_DATA[2] = {0x00,0x00};//default Value = 0x0000

#define CFR_ADD 0x03   //CFR ͨ�����ܼĴ���,��ϸ��μ�AD9958 datasheet Table 28																		
uchar CFR_DATA[3] = {0x00,0x03,0x02};//default Value = 0x000302	   

#define CFTW0_ADD 0x04   //CTW0 ͨ��Ƶ��ת���ּĴ���,��ϸ��μ�AD9958 datasheet Table 28		
uchar CFTW0_DATA0[4] = {0x33,0x33,0x33,0x33};   //OUT0 100MHZ	   ��Ƶ500M
uchar CFTW0_DATA1[4] = {0x28,0xF5,0xC2,0x8F};  //OUT1 80MHZ 
uchar CFTW0_DATA2[4] = {0x05,0x1E,0xB8,0x52};  //OUT2 10MHZ
uchar CFTW0_DATA3[4] = {0x00,0x83,0x12,0x6F};   //OUT3 1MHZ	

#define CPOW0_ADD 0x05   //CPW0 ͨ����λת���ּĴ���,��ϸ��μ�AD9958 datasheet Table 28																		
uchar CPOW0_DATA[2] = {0x00,0x00};//default Value = 0x0000   @ = POW/2^14*360

#define ACR_ADD 0x06   //ACR ���ȿ��ƼĴ���,��ϸ��μ�AD9958 datasheet Table 28																		
uchar ACR_DATA[3] = {0x00,0x00,0x00};//default Value = 0x--0000 Rest = 18.91/Iout 

#define LSRR_ADD 0x07   //LSR ͨ������ɨ��Ĵ���,��ϸ��μ�AD9958 datasheet Table 28																		
uchar LSRR_DATA[2] = {0x00,0x00};//default Value = 0x----


#define RDW_ADD 0x08   //RDW ͨ����������ɨ��Ĵ���,��ϸ��μ�AD9958 datasheet Table 28																		
uchar RDW_DATA[4] = {0x00,0x00,0x00,0x00};//default Value = 0x--------

#define FDW_ADD 0x09   //FDW ͨ����������ɨ��Ĵ���,��ϸ��μ�AD9958 datasheet Table 28																		
uchar FDW_DATA[4] = {0x00,0x00,0x00,0x00};//default Value = 0x--------

void delay1 (unsigned long   length)
{
	length = length*12;
   while(length--);
}

void IntReset(void)	  //AD9910��λ
{
    Reset = 0;
	delay1(1);
	Reset = 1;
	delay1(30);
	Reset = 0;
}

void IO_Update(void)   //AD9959��������
{
	UPDATE = 0;
	delay1(2);
	UPDATE = 1;
	delay1(4);
	UPDATE = 0;
}

void Intserve(void)		   //IO�ڳ�ʼ��
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
//�������ܣ�������ͨ��SPI��AD9959д����

//RegisterAddress ---- �Ĵ�����ַ
//NumberofRegisters---- �����ֽ���
//*RegisterData ----- ������ʼ��ַ
//temp ----- �Ƿ����IO�Ĵ���
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

          WriteData_AD9959(CSR_ADD,1,CSR_DATA0,1);//���ƼĴ���д��CH0ͨ��
          WriteData_AD9959(CFTW0_ADD,4,CFTW0_DATA0,1);//CTW0 address 0x04.���CH0�趨Ƶ��

		  WriteData_AD9959(CSR_ADD,1,CSR_DATA1,1);//���ƼĴ���д��CH1ͨ��
          WriteData_AD9959(CFTW0_ADD,4,CFTW0_DATA1,1);//CTW0 address 0x04.���CH1�趨Ƶ��
		  	
		  WriteData_AD9959(CSR_ADD,1,CSR_DATA2,1);//���ƼĴ���д��CH1ͨ��
          WriteData_AD9959(CFTW0_ADD,4,CFTW0_DATA2,1);//CTW0 address 0x04.���CH1�趨Ƶ��
		  	
		  WriteData_AD9959(CSR_ADD,1,CSR_DATA3,1);//���ƼĴ���д��CH1ͨ��
          WriteData_AD9959(CFTW0_ADD,4,CFTW0_DATA3,1);//CTW0 address 0x04.���CH1�趨Ƶ��	

} 
  
//=====================================================================
//===================����Ƶƫ�֡�Ƶ���ֺͷ��ͳ���======================
void Write_frequence(uchar Channel,ulong Freq)
{	 uchar CFTW0_DATA[4] ={0x00,0x00,0x00,0x00};	//�м����
	  ulong Temp;            
	  Temp=(ulong)Freq*8.589934592;	   //������Ƶ�����ӷ�Ϊ�ĸ��ֽ�  4.294967296=(2^32)/500000000
	  CFTW0_DATA[3]=(uchar)Temp;
	  CFTW0_DATA[2]=(uchar)(Temp>>8);
	  CFTW0_DATA[1]=(uchar)(Temp>>16);
	  CFTW0_DATA[0]=(uchar)(Temp>>24);
	  if(Channel==0)	  
	     {WriteData_AD9959(CSR_ADD,1,CSR_DATA0,1);//���ƼĴ���д��CH0ͨ��
          WriteData_AD9959(CFTW0_ADD,4,CFTW0_DATA,1);//CTW0 address 0x04.���CH0�趨Ƶ��
		 }
	  if(Channel==1)	
	   {WriteData_AD9959(CSR_ADD,1,CSR_DATA1,1);//���ƼĴ���д��CH1ͨ��
          WriteData_AD9959(CFTW0_ADD,4,CFTW0_DATA,1);//CTW0 address 0x04.���CH1�趨Ƶ��	
	   }
	  if(Channel==2)	
	   {WriteData_AD9959(CSR_ADD,1,CSR_DATA2,1);//���ƼĴ���д��CH2ͨ��
          WriteData_AD9959(CFTW0_ADD,4,CFTW0_DATA,1);//CTW0 address 0x04.���CH2�趨Ƶ��	
	   }
	  if(Channel==3)	
	   {WriteData_AD9959(CSR_ADD,1,CSR_DATA3,1);//���ƼĴ���д��CH3ͨ��
        WriteData_AD9959(CFTW0_ADD,4,CFTW0_DATA,3);//CTW0 address 0x04.���CH3�趨Ƶ��	
	   }																																																																										 
	
} 

//==============���·���====================================

void Write_Amplitude(uchar Channel, unsigned  int  Ampli)
{ uint A_temp;//=0x23ff;
  A_temp=Ampli|0x1000;
 ACR_DATA[2]=(uchar)A_temp;  //��λ����
   ACR_DATA[1]=(uchar)(A_temp>>8); //��λ����
  if(Channel==0)
     {WriteData_AD9959(CSR_ADD,1,CSR_DATA0,1); //���ƼĴ���д��CH0ͨ��
      WriteData_AD9959(ACR_ADD,3,ACR_DATA,1); //
	  }
  if(Channel==1)
     {WriteData_AD9959(CSR_ADD,1,CSR_DATA1,1); //���ƼĴ���д��CH1ͨ��
      WriteData_AD9959(ACR_ADD,3,ACR_DATA,1); //
	 }
  if(Channel==2)
     {WriteData_AD9959(CSR_ADD,1,CSR_DATA2,1); //���ƼĴ���д��CH2ͨ��
      WriteData_AD9959(ACR_ADD,3,ACR_DATA,1); //
	 }
  if(Channel==3)
     {WriteData_AD9959(CSR_ADD,1,CSR_DATA3,1); //���ƼĴ���д��CH3ͨ��
      WriteData_AD9959(ACR_ADD,3,ACR_DATA,1); //
	 }
} 
void Write_Phase(uchar Channel,unsigned  int  Phase)
{uint P_temp=0;
 P_temp=(uint)Phase*45.511111;//��������λ��д�룬����1�ȣ�45.511111=2^14��/360
 CPOW0_DATA[1]=(uchar)P_temp;
 CPOW0_DATA[0]=(uchar)(P_temp>>8);
 if(Channel==0)
  {WriteData_AD9959(CSR_ADD,1,CSR_DATA0,0); //���ƼĴ���д��CH0ͨ��																																						   //CH0 �ر� and CH1 �� ���ݴ���Ӹ�λ����λ	
   WriteData_AD9959(CPOW0_ADD,2,CPOW0_DATA,0);
  }
  if(Channel==1)
  {WriteData_AD9959(CSR_ADD,1,CSR_DATA1,0); //���ƼĴ���д��CH0ͨ��																																						   //CH0 �ر� and CH1 �� ���ݴ���Ӹ�λ����λ	
   WriteData_AD9959(CPOW0_ADD,2,CPOW0_DATA,0);
  }
  if(Channel==2)
  {WriteData_AD9959(CSR_ADD,1,CSR_DATA2,0); //���ƼĴ���д��CH0ͨ��																																						   //CH0 �ر� and CH1 �� ���ݴ���Ӹ�λ����λ	
   WriteData_AD9959(CPOW0_ADD,2,CPOW0_DATA,0);
  }
  if(Channel==3)
  {WriteData_AD9959(CSR_ADD,1,CSR_DATA3,0); //���ƼĴ���д��CH0ͨ��																																						   //CH0 �ر� and CH1 �� ���ݴ���Ӹ�λ����λ	
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